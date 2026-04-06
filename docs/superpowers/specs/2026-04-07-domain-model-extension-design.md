# Domain Model Extension Design

**Date:** 2026-04-07
**Status:** Approved
**Purpose:** Extend the Todo domain model with Categories and richer Task fields (due date, notes, progress) to support the Web UI and native app designs.

---

## 1. New Aggregates

### Category

User-scoped aggregate. Each user gets a pre-seeded default set on first login.

```csharp
public class Category
{
    public Guid Id { get; private set; }
    public string UserId { get; private set; }   // ASP.NET Core Identity user ID
    public string Name { get; private set; }
    public string Color { get; private set; }    // hex e.g. "#8B5CF6"
    public string Icon { get; private set; }     // Material Symbols name e.g. "work"
    public int Order { get; private set; }       // display order
    public DateTimeOffset CreatedAt { get; private set; }
}
```

**Default seed categories** (created on first login via a domain event handler):

| Name     | Color     | Icon        |
|----------|-----------|-------------|
| Personal | `#8B5CF6` | `person`    |
| Work     | `#F59E0B` | `work`      |
| Urgent   | `#EF4444` | `priority_high` |
| Design   | `#0EA5E9` | `palette`   |

---

## 2. Extended Todo Aggregate

New nullable fields added to the existing `Todo` aggregate:

```csharp
public Guid? CategoryId { get; private set; }          // FK to Category, nullable
public DateTimeOffset? DueDate { get; private set; }
public string? Notes { get; private set; }             // freetext, no max enforced at domain
public int Progress { get; private set; }              // 0–100, default 0
```

All new fields are nullable/defaulted so existing todos require no migration data fix-up.

---

## 3. Domain Events

### New events

```
CategoryCreated      { CategoryId, UserId, Name, Color, Icon, Order }
CategoryUpdated      { CategoryId, Name?, Color?, Icon?, Order? }
CategoryDeleted      { CategoryId }
TodoCategoryAssigned { TodoId, CategoryId? }           // null = unassigned
TodoDueDateSet       { TodoId, DueDate? }              // null = cleared
TodoNotesUpdated     { TodoId, Notes? }
TodoProgressUpdated  { TodoId, Progress }
UserSeeded           { UserId }                        // triggers default category creation
```

---

## 4. API Changes

### New endpoints

```
GET    /categories                        → 200 [{ id, name, color, icon, order }]
POST   /categories                        → 202 + Location + X-Retry-After-Ms
PUT    /categories/{id}                   → 202 + Location + X-Retry-After-Ms
DELETE /categories/{id}                   → 202 + Location + X-Retry-After-Ms
```

### Extended todo endpoints

`POST /todos` request body gains optional fields:

```json
{
  "title": "string",
  "categoryId": "guid?",
  "dueDate": "ISO8601?",
  "notes": "string?",
  "progress": "int? (0–100)"
}
```

New endpoint to update any todo field:

```
PUT /todos/{id}     → 202 + Location + X-Retry-After-Ms
```

Body accepts any combination of: `title`, `categoryId`, `dueDate`, `notes`, `progress`. Existing `POST /todos/{id}/complete` and `DELETE /todos/{id}` are unchanged.

### Conflict response

All mutating endpoints (`POST /todos`, `PUT /todos/{id}`, `POST /todos/{id}/complete`, `DELETE /todos/{id}`, `POST /categories`, `PUT /categories/{id}`, `DELETE /categories/{id}`) return `409 Conflict` when the client's `expectedVersion` header does not match the server's current aggregate version:

```
Request header:  X-Expected-Version: {int}
409 response:    { commandId, aggregateId, serverEvents: [{ id, aggregateId, aggregateVersion, type, payload, timestamp }] }
```

The client uses the returned `serverEvents` to resolve the conflict locally (see PWA/Offline Design spec).

---

## 5. Read Models

### TodoSummary (API response)

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

### CategorySummary (API response)

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

---

## 6. Database Migration

One EF Core migration: `AddCategoriesAndExtendTodos`

- New table: `Categories` (`Id`, `UserId`, `Name`, `Color`, `Icon`, `Order`, `CreatedAt`)
- New columns on `Todos`: `CategoryId` (nullable FK), `DueDate`, `Notes`, `Progress`
- FK: `Todos.CategoryId → Categories.Id` with `ON DELETE SET NULL`
- Index: `Categories(UserId)`, `Todos(CategoryId)`

Non-breaking. All new columns nullable; no existing row requires update.

---

## 7. Validation

- `Category.Name`: required, max 50 chars
- `Category.Color`: required, must match `^#[0-9A-Fa-f]{6}$`
- `Category.Icon`: required, max 50 chars
- `Todo.Progress`: 0–100 inclusive
- `Todo.Notes`: max 2000 chars
- A `CategoryDeleted` command with todos still assigned: todos have `CategoryId` set to null via cascade (`ON DELETE SET NULL`), no error returned to caller
