# Azure AD (Entra ID) App Registration for M365

This guide explains how to configure an Azure AD app registration so Migrify can access Microsoft 365 mailboxes via the Microsoft Graph API.

## Prerequisites

- An Azure account with access to [Microsoft Entra admin center](https://entra.microsoft.com/)
- Global Administrator or Application Administrator role in the tenant
- The M365 tenant where the destination mailboxes are located

## Step 1: Register the Application

1. Go to **Microsoft Entra admin center** → **Identity** → **Applications** → **App registrations**
2. Click **New registration**
3. Fill in:
   - **Name**: Migrify
   - **Supported account types**: Accounts in this organizational directory only (Single tenant)
   - **Redirect URI**: Leave empty (not needed for app-only auth)
4. Click **Register**
5. Note the **Application (client) ID** and **Directory (tenant) ID** from the Overview page

## Step 2: Configure API Permissions

1. In your app registration, go to **API permissions**
2. Click **Add a permission** → **Microsoft Graph** → **Application permissions**
3. Search and add these permissions:
   - **Mail.ReadWrite** — Read and write mail in all mailboxes (required for migration)
   - **User.Read.All** — Read all users' full profiles (required for bulk mailbox discovery in a later version)
4. Click **Add permissions**
5. Click **Grant admin consent for [your organization]**
6. Verify that both permissions show a green checkmark under "Status"

> **Important**: Application permissions (not delegated) are required because Migrify accesses mailboxes without a signed-in user. Admin consent is mandatory for application permissions.

## Step 3: Create a Client Secret

1. Go to **Certificates & secrets** → **Client secrets**
2. Click **New client secret**
3. Fill in:
   - **Description**: Migrify
   - **Expires**: Choose an appropriate duration (recommended: 24 months)
4. Click **Add**
5. **Immediately copy the secret Value** (it won't be shown again)

> **Warning**: Store the client secret securely. If you lose it, you'll need to create a new one.

## Step 4: Configure in Migrify

1. Open your project in Migrify
2. On the project detail page, click **Configure** on the **Destination Connector** card
3. Enter:
   - **Tenant ID**: The Directory (tenant) ID from Step 1
   - **Client ID**: The Application (client) ID from Step 1
   - **Client Secret**: The secret value from Step 3
4. Click **Test Connection** to verify
5. Click **Save**

## Step 5: Verify Permissions

When you click **Test Connection** with a destination email address configured:
- Migrify first validates that the credentials are correct
- Then it verifies that `Mail.ReadWrite` permission works by reading the mailbox folders
- If credentials are valid but permissions are missing, you'll get a specific error message

You can also click **Explore M365 Mailbox** on the project detail page to see all folders and message counts.

## Troubleshooting

### "Credentials are valid, but the app lacks Mail.ReadWrite permission"
The app registration doesn't have the required permissions, or admin consent hasn't been granted. Go back to Step 2 and verify.

### "AADSTS7000215: Invalid client secret"
The client secret is expired or incorrect. Create a new one in Step 3.

### "AADSTS700016: Application not found in the directory"
The Client ID is wrong, or the app was registered in a different tenant. Verify the Tenant ID and Client ID match.

### "Authorization_RequestDenied"
The app doesn't have the required permissions for the specific operation. Ensure both `Mail.ReadWrite` and `User.Read.All` are granted with admin consent.

### "ResourceNotFound" or "MailboxNotFound"
The destination email address doesn't exist in the tenant, or the mailbox hasn't been provisioned yet.

## Security Notes

- Migrify encrypts the client secret at rest using AES-256-GCM
- The encryption key is stored in the `MIGRIFY_ENCRYPTION_KEY` environment variable
- Client secrets are never logged or displayed in the UI after saving
- Consider using a dedicated app registration per tenant for better isolation
- Review and rotate client secrets before they expire
