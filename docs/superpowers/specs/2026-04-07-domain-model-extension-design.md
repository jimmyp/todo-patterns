# Domain Model Extension Design

**Date:** 2026-04-07
**Status:** Approved
**Purpose:** Extend the Todo domain model with Categories and richer Task fields (due date, notes, progress) to support the Web UI and native app designs.

---

## 1. Aggregates

### CategoryList

The user's full set of categories is the aggregate — not individual categories. The invariant (category names must be unique per user) belongs to the collection. `CategoryList` is owned by a single user.

```csharp
public class CategoryList
{
    public string UserId { get; private set; }         // aggregate ID = user ID
    public int Version { get; private set; }
    public IReadOnlyList<Category> Categories { get; private set; }

    // Invariant: names unique within this list
    public DomainResult<CategoryAddedEvent> AddCategory(string name, string color, string icon) { ... }
    public DomainResult<CategoryRenamedEvent> RenameCategory(Guid id, string newName) { ... }
    public DomainResult<CategoryColorChangedEvent> ChangeColor(Guid id, string color) { ... }
    public DomainResult<CategoryIconChangedEvent> ChangeIcon(Guid id, string icon) { ... }
    public DomainResult<CategoryReorderedEvent> Reorder(Guid id, int newOrder) { ... }
    public DomainResult<CategoryRemovedEvent> RemoveCategory(Guid id) { ... }
}

public record Category(Guid Id, string Name, string Color, string Icon, int Order, DateTimeOffset CreatedAt);
```

**Default seed categories** (created on first login via a `UserSeeded` event handler):

| Name     | Color     | Icon            |
|----------|-----------|-----------------|
| Personal | `#8B5CF6` | `person`        |
| Work     | `#F59E0B` | `work`          |
| Urgent   | `#EF4444` | `priority_high` |
| Design   | `#0EA5E9` | `palette`       |

### Todo (extended)

New nullable fields added to the existing `Todo` aggregate:

```csharp
public Guid? CategoryId { get; private set; }
public DateTimeOffset? DueDate { get; private set; }
public string? Notes { get; private set; }
public int Progress { get; private set; }              // 0–100, default 0
```

All new fields are nullable/defaulted so existing todos require no migration data fix-up.

---

## 2. Domain Events

All events carry intent — no generic "updated" events.

### CategoryList events

```
CategoryAdded        { UserId, CategoryId, Name, Color, Icon, Order }
CategoryRenamed      { UserId, CategoryId, NewName }
CategoryColorChanged { UserId, CategoryId, NewColor }
CategoryIconChanged  { UserId, CategoryId, NewIcon }
CategoryReordered    { UserId, CategoryId, NewOrder }
CategoryRemoved      { UserId, CategoryId }
UserSeeded           { UserId }                        // triggers default category seeding
```

### Todo events (new)

```
TodoCategoryAssigned   { TodoId, CategoryId }
TodoCategoryUnassigned { TodoId }
TodoDueDateSet         { TodoId, DueDate }
TodoDueDateCleared     { TodoId }
TodoNotesUpdated       { TodoId, Notes }               // notes is a free-text blob, no sub-intent
TodoProgressUpdated    { TodoId, Progress }
TodoRenamed            { TodoId, NewTitle }
```

---

## 3. API — Commands (mutating endpoints)

All mutating endpoints follow the existing async command pattern: `202 Accepted + Location + X-Retry-After-Ms`. Each maps to a single command with clear intent.

### Category endpoints

```
POST   /categories                         body: { name, color, icon }
POST   /categories/{id}/rename             body: { name }
POST   /categories/{id}/change-color       body: { color }
POST   /categories/{id}/change-icon        body: { icon }
POST   /categories/{id}/reorder            body: { order }
DELETE /categories/{id}
```

### Todo endpoints (new + extended)

```
POST   /todos                              body: { title, categoryId?, dueDate?, notes?, progress? }
POST   /todos/{id}/rename                  body: { title }
POST   /todos/{id}/assign-category         body: { categoryId }
POST   /todos/{id}/unassign-category
POST   /todos/{id}/set-due-date            body: { dueDate }
POST   /todos/{id}/clear-due-date
POST   /todos/{id}/update-notes            body: { notes }
POST   /todos/{id}/update-progress         body: { progress }
POST   /todos/{id}/complete                (existing, unchanged)
DELETE /todos/{id}                         (existing, unchanged)
```

`POST /todos` still accepts optional fields for convenience — it dispatches multiple commands internally if optional fields are provided.

### Conflict response

All mutating endpoints include an `X-Expected-Version` request header. If the server's current aggregate version doesn't match, the server returns:

```
Request header:  X-Expected-Version: {int}
409 response:    { commandId, aggregateId, serverEvents: [{ id, aggregateId, aggregateVersion, type, payload, timestamp }] }
```

The client uses the returned `serverEvents` to resolve the conflict locally (see PWA/Offline Design spec).

---

## 4. API — Read Models (GET endpoints)

GET endpoints serve pre-projected read model tables — they do not query aggregates. Projections are updated by event handlers in response to domain events.

### GET /todos → TodoSummary[]

Projection table `TodoSummaries`, updated by handlers for all Todo and CategoryList events.

```json
{
  "id": "guid",
  "title": "string",
  "isCompleted": "bool",
  "categoryId": "guid?",
  "categoryName": "string?",
  "categoryColor": "string?",
  "dueDate": "ISO8601?",
  "isOverdue": "bool",
  "progress": "int",
  "createdAt": "ISO8601",
  "completedAt": "ISO8601?"
}
```

### GET /categories → CategorySummary[]

Projection table `CategorySummaries`, updated by handlers for all CategoryList events.

```json
{
  "id": "guid",
  "name": "string",
  "color": "string",
  "icon": "string",
  "order": "int",
  "todoCount": "int"
}
```

`todoCount` is maintained by incrementing/decrementing on `TodoCreated`, `TodoDeleted`, `TodoCategoryAssigned`, `TodoCategoryUnassigned`.

---

## 5. Database Migration

One EF Core migration: `AddCategoriesAndExtendTodos`

- New table: `CategoryLists` (`UserId` PK, `Version`)
- New table: `Categories` (`Id`, `UserId` FK, `Name`, `Color`, `Icon`, `Order`, `CreatedAt`)
- New table: `TodoSummaries` (projection — `Id`, `UserId`, `Title`, `IsCompleted`, `CategoryId?`, `CategoryName?`, `CategoryColor?`, `DueDate?`, `IsOverdue`, `Progress`, `CreatedAt`, `CompletedAt?`)
- New table: `CategorySummaries` (projection — `Id`, `UserId`, `Name`, `Color`, `Icon`, `Order`, `TodoCount`)
- New columns on `Todos` aggregate table: `CategoryId` (nullable FK), `DueDate`, `Notes`, `Progress`
- FK: `Todos.CategoryId → Categories.Id` with `ON DELETE SET NULL`
- Index: `Categories(UserId)`, `TodoSummaries(UserId)`, `CategorySummaries(UserId)`

Non-breaking. All new columns nullable; no existing row requires update.

---

## 6. Validation

- `Category.Name`: required, max 50 chars, unique within the user's `CategoryList`
- `Category.Color`: required, must match `^#[0-9A-Fa-f]{6}$`
- `Category.Icon`: required, max 50 chars
- `Todo.Progress`: 0–100 inclusive
- `Todo.Notes`: max 2000 chars
- `CategoryRemoved` with todos still assigned: `TodoCategoryUnassigned` events are raised for each affected todo as part of the same operation — no orphaned category references
