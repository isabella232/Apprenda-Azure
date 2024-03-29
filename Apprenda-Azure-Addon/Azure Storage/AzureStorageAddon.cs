﻿using System;
using System.Net;
using System.Threading;
using Microsoft.Azure;
using Microsoft.WindowsAzure.Management.Storage;
using Microsoft.WindowsAzure.Management.Storage.Models;

namespace Apprenda.SaaSGrid.Addons.Azure.Storage
{
    public class AzureStorageAddon : AddonBase
    {
        public override ProvisionAddOnResult Provision(AddonProvisionRequest request)
        {
            var provisionResult = new ProvisionAddOnResult("");
            try
            {
                var devParameters = DeveloperParameters.Parse(request.DeveloperParameters, request.Manifest.GetProperties());

                SubscriptionCloudCredentials creds = CertificateAuthenticationHelper.GetCredentials(devParameters.AzureManagementSubscriptionId, devParameters.AzureAuthenticationKey, devParameters.PFXPassword);

                // ok so if we need a storage account, we need to use the storage management client.
                if (devParameters.NewStorageAccountFlag)
                { 
                    var client = new StorageManagementClient(creds);
                    var parameters = CreateStorageAccountParameters(devParameters);
                    client.StorageAccounts.Create(parameters);
                    do
                    {
                        var verificationResponse = client.StorageAccounts.Get(parameters.Name);

                        if (verificationResponse.StorageAccount.Properties.Status.Equals(StorageAccountStatus.Created))
                        {
                            var azureconnectioninfo = client.StorageAccounts.Get(devParameters.StorageAccountName);
                            var keysForStorageUnit = client.StorageAccounts.GetKeys(devParameters.StorageAccountName);

                            var connectionInfo = new ConnectionInfo
                            {
                                PrimaryKey = keysForStorageUnit.PrimaryKey,
                                SecondaryKey = keysForStorageUnit.SecondaryKey,
                                StorageAccountName = azureconnectioninfo.StorageAccount.Name,
                                Uri = keysForStorageUnit.Uri.ToString()
                            };
                            provisionResult.ConnectionData = connectionInfo.ToString();
                            provisionResult.IsSuccess = true;
                            break;
                        }
                        Thread.Sleep(TimeSpan.FromSeconds(10d));
                    } while (true);
                }
            }
            catch (Exception e)
            {
                provisionResult.IsSuccess = false;
                provisionResult.EndUserMessage = e.Message + "\n We're in an error\n";
            }

            return provisionResult;
        }

        private static StorageAccountCreateParameters CreateStorageAccountParameters(DeveloperParameters developerOptions)
        {
            var parameters = new StorageAccountCreateParameters
            {
                Description = developerOptions.Description,
                Location = developerOptions.Location,
                Label = developerOptions.StorageAccountName,
                Name = developerOptions.StorageAccountName,
                AccountType = "Standard_LRS"
            };
            if (developerOptions.AffinityGroup != null)
            {
                parameters.AffinityGroup = developerOptions.AffinityGroup;
            }
            else if (developerOptions.Location != null)
            {
                parameters.Location = developerOptions.Location;
            }
            else
            {
                throw new ArgumentException("Must have a value for either AffinityGroup or Location. Please verify your settings in the manifest file.");
            }
            return parameters;
        }

        public override OperationResult Deprovision(AddonDeprovisionRequest request)
        {
            var connectionData = request.ConnectionData;
            var deprovisionResult = new ProvisionAddOnResult(connectionData);
            var devOptions = DeveloperParameters.Parse(request.DeveloperParameters, request.Manifest.GetProperties());
            // set up the credentials for azure
            SubscriptionCloudCredentials creds = CertificateAuthenticationHelper.GetCredentials(devOptions.AzureManagementSubscriptionId, devOptions.AzureAuthenticationKey, devOptions.PFXPassword);
            // set up the storage management client
            var client = new StorageManagementClient(creds);

            // check to see if the storage accounts exists...if they do not exist, throw a no-op here.
            var account = client.StorageAccounts.Get(devOptions.StorageAccountName);
            if (account.StatusCode.Equals(HttpStatusCode.NotFound))
            {
                // no account found. return a no-op as something went wrong, proceed to deletion
                deprovisionResult.IsSuccess = true;
                deprovisionResult.EndUserMessage = "Storage account not found, may have been deleted via 3rd party.";
                return deprovisionResult;
            }
            
            // then if requested, delete the storage account name
            client.StorageAccounts.Delete(devOptions.StorageAccountName);

            do
            {
                var verificationResponse = client.StorageAccounts.Get(devOptions.StorageAccountName);

                if (verificationResponse.StorageAccount.Properties.Status.Equals(StorageAccountStatus.Deleting))
                {
                    deprovisionResult.IsSuccess = true;
                    deprovisionResult.EndUserMessage = "Deprovision Request Complete, please allow a few minutes for resources to be fully deleted.";
                    break;
                }
                Thread.Sleep(TimeSpan.FromSeconds(10d));
            }
            while (true);
            return deprovisionResult;
        }

        public override OperationResult Test(AddonTestRequest request)
        {
            var manifest = request.Manifest;
            var developerParams = request.DeveloperParameters;
            var testResult = new OperationResult { IsSuccess = false };
            var testProgress = "";
            var manifestProperties = manifest.Properties;

            if (manifestProperties != null)
            {
                var devOptions = DeveloperParameters.Parse(developerParams, manifest.GetProperties());
                var creds = CertificateAuthenticationHelper.GetCredentials(devOptions.AzureManagementSubscriptionId,
                    devOptions.AzureAuthenticationKey, devOptions.PFXPassword);
                try
                {
                    testProgress += "Establishing connection to Azure...\n";
                    // set up the credentials for azure

                    var client = new StorageManagementClient(creds);

                    //var listOfStorageAccounts = client.StorageAccounts.List();

                    testProgress += "Successfully passed all testing criteria!";
                    testResult.IsSuccess = true;
                    testResult.EndUserMessage = testProgress;
                }
                catch (Exception e)
                {
                    // adding a forced failure here.
                    testResult.IsSuccess = false;
                    testResult.EndUserMessage = testProgress + "\nEXCEPTION: " + e.Message;
                }
            }
            else
            {
                testResult.IsSuccess = false;
                testResult.EndUserMessage = "Missing required manifest properties (requireDevCredentials)";
            }

            return testResult;
        }
    }
}
