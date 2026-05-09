# Setup Instructions & Credentials

To make your pipelines work, you need to configure the following secrets and variables.

## 1. GitHub Actions Secrets
Go to your GitHub Repository -> Settings -> Secrets and variables -> Actions -> **New repository secret**.

| Secret Name | Value Description |
| :--- | :--- |
| `OCTOPUS_SERVER_URL` | The URL of your Octopus Deploy instance (e.g., `https://your-instance.octopus.app`). |
| `OCTOPUS_API_KEY` | An API Key created in your Octopus Profile. |

## 2. Octopus Deploy Configuration
You need to set up the following in your Octopus Deploy project.

### Infrastructure
1.  **Environments:** Create an environment named `Production`.
2.  **Accounts:** Add your **Azure Subscription** as an Account in Octopus.
    *   Go to *Infrastructure* -> *Accounts* -> *Add Account* -> *Azure Subscription*.
    *   Follow the instructions to register the Application ID, Tenant ID, and Key from Azure.

### Project Variables
Go to your Project -> Variables and add these:

| Variable Name | Value |
| :--- | :--- |
| `Azure.Account` | Select the Azure Account you created in the step above (Infrastructure). |
| `Azure.ResourceGroup` | The name of the Resource Group in Azure where your App Service lives (e.g., `rg-todolist`). |
| `Azure.WebAppName` | The name of your App Service in Azure (e.g., `app-todolist-dev`). |
| `WorkerPool` | The name of the worker pool to run the deployment (e.g., `Hosted Windows` or `Default Worker Pool`). |

## 3. Azure Setup
Since you are using the **Free Tier (F1)**:
1.  Create an **App Service Plan** in Azure using the **F1 (Free)** pricing tier.
    *   *Note: Ensure you choose "Linux" or "Windows" to match your preference, though .NET works on both. This pipeline assumes a standard Web App deployment.*
2.  Create an **App Service** (Web App) linked to that plan.
