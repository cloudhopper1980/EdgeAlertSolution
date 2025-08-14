# Azure Configuration Guide

## Setting up Azure AD Authentication

The EdgeAlert system requires Azure AD authentication to access Key Vault and other Azure resources. Follow these steps to configure the Azure AD client secret securely.

## Required Azure Resources
- Azure AD Application Registration
- Azure Key Vault (already configured: `edgestoragevault`)
- Appropriate permissions for the AD application

## Configuration Methods

### Method 1: Environment Variables (Recommended for Development)

Set the following environment variables on your system:

```bash
# Windows (Command Prompt)
set AZURE_CLIENT_SECRET=your_new_client_secret_here
set AZURE_TENANT_ID=4ab91383-3140-4414-942f-74e6dfe5035f
set AZURE_CLIENT_ID=187b13e7-ad4d-486e-86a1-4ae15dd5b65d
set AZURE_KEYVAULT_URI=https://edgestoragevault.vault.azure.net/

# Windows (PowerShell)
$env:AZURE_CLIENT_SECRET="your_new_client_secret_here"
$env:AZURE_TENANT_ID="4ab91383-3140-4414-942f-74e6dfe5035f"
$env:AZURE_CLIENT_ID="187b13e7-ad4d-486e-86a1-4ae15dd5b65d"
$env:AZURE_KEYVAULT_URI="https://edgestoragevault.vault.azure.net/"

# Linux/macOS
export AZURE_CLIENT_SECRET="your_new_client_secret_here"
export AZURE_TENANT_ID="4ab91383-3140-4414-942f-74e6dfe5035f"
export AZURE_CLIENT_ID="187b13e7-ad4d-486e-86a1-4ae15dd5b65d"
export AZURE_KEYVAULT_URI="https://edgestoragevault.vault.azure.net/"
```

### Method 2: User Secrets (Development Only)

For local development, you can use .NET User Secrets:

```bash
# Initialize user secrets for the project
cd EdgeAlertSignalClient
dotnet user-secrets init

# Set the secrets
dotnet user-secrets set "Azure:ClientSecret" "your_new_client_secret_here"
dotnet user-secrets set "Azure:TenantId" "4ab91383-3140-4414-942f-74e6dfe5035f"
dotnet user-secrets set "Azure:ClientId" "187b13e7-ad4d-486e-86a1-4ae15dd5b65d"
dotnet user-secrets set "Azure:KeyVaultUri" "https://edgestoragevault.vault.azure.net/"
```

### Method 3: Azure Key Vault (Production - Recommended)

For production, store the client secret in Azure Key Vault itself and use Managed Identity or Azure AD authentication to retrieve it.

### Method 4: Configuration File (Local Testing Only - NOT for Production)

Create a file `appsettings.Development.json` in the EdgeAlertSignalClient folder:

```json
{
  "Azure": {
    "ClientSecret": "your_new_client_secret_here",
    "TenantId": "4ab91383-3140-4414-942f-74e6dfe5035f",
    "ClientId": "187b13e7-ad4d-486e-86a1-4ae15dd5b65d",
    "KeyVaultUri": "https://edgestoragevault.vault.azure.net/"
  }
}
```

**IMPORTANT**: Add `appsettings.Development.json` to `.gitignore` to prevent accidental commits!

## Generating a New Client Secret

Since the previous secret was exposed, you need to generate a new one:

1. Go to [Azure Portal](https://portal.azure.com)
2. Navigate to Azure Active Directory → App registrations
3. Find your application (Client ID: `187b13e7-ad4d-486e-86a1-4ae15dd5b65d`)
4. Go to "Certificates & secrets"
5. Delete the compromised secret
6. Click "New client secret"
7. Add a description and set expiration
8. Copy the new secret value immediately (it won't be shown again)
9. Configure it using one of the methods above

## Security Best Practices

1. **Never commit secrets to source control**
2. **Use different secrets for different environments** (dev, staging, production)
3. **Rotate secrets regularly** (at least every 90 days)
4. **Use Managed Identity where possible** to eliminate secrets
5. **Monitor Azure AD sign-in logs** for unauthorized access
6. **Set secret expiration dates** in Azure AD

## Verifying Configuration

After setting up the configuration, test the application:

```bash
# Run the WPF application
cd EdgeAlertSignalClient
dotnet run

# Check logs for successful Key Vault connection
# Look for: "AzureHandler created. Initialization task setup."
# And no warnings about missing client secret
```

## Troubleshooting

If you see the warning "Azure Client Secret not configured":
1. Verify environment variable is set correctly
2. Restart your IDE/terminal to pick up new environment variables
3. Check the exact environment variable name matches: `AZURE_CLIENT_SECRET`

## For Docker/Container Deployment

Add to your Dockerfile or docker-compose.yml:

```dockerfile
ENV AZURE_CLIENT_SECRET=your_secret_here
# Or better, use Docker secrets or Azure Key Vault
```

```yaml
# docker-compose.yml
services:
  edgealert:
    environment:
      - AZURE_CLIENT_SECRET=${AZURE_CLIENT_SECRET}
      - AZURE_TENANT_ID=4ab91383-3140-4414-942f-74e6dfe5035f
      - AZURE_CLIENT_ID=187b13e7-ad4d-486e-86a1-4ae15dd5b65d
```

## Additional Resources

- [Azure AD App Registration Documentation](https://docs.microsoft.com/en-us/azure/active-directory/develop/quickstart-register-app)
- [Azure Key Vault Best Practices](https://docs.microsoft.com/en-us/azure/key-vault/general/best-practices)
- [.NET Configuration Documentation](https://docs.microsoft.com/en-us/dotnet/core/extensions/configuration)