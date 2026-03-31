# Google OAuth2 Setup for IMAP

This guide explains how to configure Google OAuth2 credentials so Migrify can connect to Gmail / Google Workspace mailboxes via IMAP.

## Prerequisites

- A Google account with access to [Google Cloud Console](https://console.cloud.google.com/)
- A Google Cloud project (create one if you don't have it)

## Step 1: Enable the Gmail API

1. Go to **APIs & Services > Library**
2. Search for **Gmail API**
3. Click **Enable**

## Step 2: Configure OAuth Consent Screen

1. Go to **APIs & Services > OAuth consent screen**
2. Choose **External** (or **Internal** for Google Workspace organizations)
3. Fill in:
   - **App name**: Migrify (or your preferred name)
   - **User support email**: your email
   - **Developer contact information**: your email
4. Click **Save and Continue**
5. On the **Scopes** page, click **Add or Remove Scopes**
6. Add the scope: `https://mail.google.com/`
7. Click **Save and Continue**
8. On **Test users**, add the Gmail accounts you want to migrate
9. Click **Save and Continue**

> **Important**: While the app is in "Testing" status, OAuth2 refresh tokens expire after 7 days. To avoid this, publish the app (click **Publish App** on the consent screen page). Google may require verification for published apps with sensitive scopes.

## Step 3: Create OAuth2 Credentials

1. Go to **APIs & Services > Credentials**
2. Click **Create Credentials > OAuth client ID**
3. Choose **Web application**
4. Fill in:
   - **Name**: Migrify
   - **Authorized redirect URIs**: `https://<your-migrify-host>/oauth/callback/google`
     - For local development: `http://localhost:<port>/oauth/callback/google`
5. Click **Create**
6. Copy the **Client ID** and **Client Secret**

## Step 4: Configure in Migrify

1. Open your project in Migrify
2. Go to the **IMAP Source** tab
3. In **Advanced Settings**, set **Authentication** to **OAuth2**
4. Enter the **Google Client ID** and **Google Client Secret**
5. Click **Save** (or **Update**)
6. Click **Authorize with Google**
7. A popup opens — sign in with the Google account you want to migrate
8. Grant access to Migrify
9. The popup closes automatically and the token status shows **Authorized**

## Step 5: Test the Connection

1. Click **Test Connection** — should show "IMAP connection successful!"
2. Click **Explore** to see mailbox folders and message counts

## Troubleshooting

### "No refresh token received"
This happens when you've previously authorized the app. Go to [Google Account Permissions](https://myaccount.google.com/permissions), revoke access for Migrify, and try again.

### "Token refresh failed. Re-authorization required."
The refresh token has expired or been revoked. Re-authorize by clicking **Authorize with Google** again.

### 403 Forbidden or "Access Not Configured"
Make sure the Gmail API is enabled in your Google Cloud project (Step 1).

### "redirect_uri_mismatch"
The redirect URI in Migrify must exactly match the one configured in Google Cloud Console, including the protocol (http/https) and port.

### Testing mode — tokens expire after 7 days
While your Google Cloud app is in "Testing" status, refresh tokens expire after 7 days. Publish the app to get long-lived tokens (see Step 2 note).
