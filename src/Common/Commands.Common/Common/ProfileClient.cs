﻿// ----------------------------------------------------------------------------------
//
// Copyright Microsoft Corporation
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// ----------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Subscriptions;
using Microsoft.Azure.Subscriptions.Models;
using Microsoft.WindowsAzure.Commands.Common.Models;
using Microsoft.WindowsAzure.Commands.Common.Properties;
using Microsoft.WindowsAzure.Commands.Utilities.Common;
using Microsoft.WindowsAzure.Commands.Utilities.Common.Authentication;
using Microsoft.WindowsAzure.Commands.Common.Interfaces;

namespace Microsoft.WindowsAzure.Commands.Common
{
    /// <summary>
    /// Convenience client for azure profile and subscriptions.
    /// </summary>
    public class ProfileClient
    {
        public static IDataStore DataStore { get; set; }

        public AzureProfile Profile { get; private set; }

        private static void UpgradeProfile()
        {
            if (DataStore.FileExists(System.IO.Path.Combine(AzurePowerShell.ProfileDirectory, AzurePowerShell.OldProfileFile)))
            {
                string oldProfilePath = System.IO.Path.Combine(AzurePowerShell.ProfileDirectory,
                    AzurePowerShell.OldProfileFile);
                AzureProfile profile = new AzureProfile(DataStore, oldProfilePath);

                // Save the profile to the disk
                profile.Save();

                // Rename WindowsAzureProfile.xml to AzureProfile.json
                DataStore.RenameFile(oldProfilePath,
                    System.IO.Path.Combine(AzurePowerShell.ProfileDirectory, AzurePowerShell.ProfileFile));
            }
        }

        static ProfileClient()
        {
            DataStore = new DiskDataStore();
        }

        public ProfileClient()
            : this(System.IO.Path.Combine(AzurePowerShell.ProfileDirectory, AzurePowerShell.ProfileFile))
        {

        }

        public ProfileClient(string profilePath)
        {
            ProfileClient.UpgradeProfile();

            Profile = new AzureProfile(DataStore, profilePath);
        }

        public AzureAccount AddAzureAccount(UserCredentials credentials, string environment)
        {
            if (string.IsNullOrEmpty(environment))
            {
                environment = AzureSession.CurrentEnvironment.Name;
            }

            if (!Profile.Environments.ContainsKey(environment))
            {
                throw new Exception(string.Format(Resources.EnvironmentNotFound, environment));
            }

            var subscriptions = LoadSubscriptionsFromServer(ref credentials).ToList();
            subscriptions.ForEach(s => s.Environment = environment);
            if (Profile.DefaultSubscription == null)
            {
                Profile.DefaultSubscription = subscriptions[0];
            }
            AddSubscriptions(subscriptions);
            return new AzureAccount {UserName = credentials.UserName, Subscriptions = subscriptions };
        }

        public IEnumerable<AzureAccount> GetAzureAccount(string userName, string environment)
        {
            List<AzureSubscription> subscriptions = Profile.Subscriptions.Values.ToList();
            if (environment != null)
            {
                subscriptions = subscriptions.Where(s => s.Environment == environment).ToList();
            }

            List<string> names = new List<string>();
            if (!string.IsNullOrEmpty(userName))
            {
                names.Add(userName);
            }
            else
            {
                names = subscriptions
                    .Where(s => s.GetProperty(AzureSubscription.Property.UserAccount) != null)
                    .Select(s => s.GetProperty(AzureSubscription.Property.UserAccount))
                    .Distinct().ToList();
            }

            foreach (var name in names)
            {
                AzureAccount account = new AzureAccount();
                account.UserName = name;
                account.Subscriptions = subscriptions
                    .Where(s => s.GetProperty(AzureSubscription.Property.UserAccount) == name).ToList();

                if (account.Subscriptions.Count == 0)
                {
                    continue;
                }

                yield return account;
            }
        }

        public AzureAccount RemoveAzureAccount(string userName, Action<string> warningLog)
        {
            var userAccounts = GetAzureAccount(userName, null);

            if (string.IsNullOrEmpty(userName))
            {
                throw new ArgumentException("User name needs to be specified.", "userName");
            }

            if (!userAccounts.Any())
            {
                throw new ArgumentException("User name is not valid.", "userName");
            }

            var userAccount = userAccounts.First();

            foreach (var subscriptionFromAccount in userAccount.Subscriptions)
            {
                var subscription = Profile.Subscriptions[subscriptionFromAccount.Id];

                // Warn the user if the removed subscription is the default one.
                if (subscription.GetProperty(AzureSubscription.Property.Default) != null)
                {
                    if (warningLog != null)
                    {
                        warningLog(Resources.RemoveDefaultSubscription);
                    }
                }

                // Warn the user if the removed subscription is the current one.
                if (subscription.Equals(AzureSession.CurrentSubscription))
                {
                    if (warningLog != null)
                    {
                        warningLog(Resources.RemoveCurrentSubscription);
                    }
                }

                Profile.Subscriptions.Remove(subscription.Id);
            }

            return userAccount;
        }

        public IEnumerable<AzureSubscription> LoadSubscriptionsFromPublishSettingsFile(string filePath)
        {
            var currentEnvironment = AzureSession.CurrentEnvironment;

            if (string.IsNullOrEmpty(filePath) || !DataStore.FileExists(filePath))
            {
                throw new ArgumentException("File path is not valid.", "filePath");
            }
            return PublishSettingsImporter.ImportAzureSubscription(DataStore.ReadFileAsStream(filePath), currentEnvironment.Name);
        }

        public IEnumerable<AzureSubscription> LoadSubscriptionsFromServer()
        {
            UserCredentials credentials = new UserCredentials { NoPrompt = true };
            return LoadSubscriptionsFromServer(ref credentials);
        }

        public IEnumerable<AzureSubscription> LoadSubscriptionsFromServer(ref UserCredentials credentials)
        {
            var currentMode = PowerShellUtilities.GetCurrentMode();
            var currentSubscription = AzureSession.CurrentSubscription;
            var currentEnvironment = AzureSession.CurrentEnvironment;
            if (currentSubscription == null)
            {
                return LoadSubscriptionsFromServer(currentEnvironment, currentMode, ref credentials);
            }
            else
            {
                // Get all AD accounts and iterate
                var userIds = Profile.Subscriptions.Values
                    .Select(s => s.Properties[AzureSubscription.Property.UserAccount]).Distinct();

                List<AzureSubscription> subscriptions = new List<AzureSubscription>();
                foreach (var userId in userIds)
                {
                    credentials.UserName = userId;
                    subscriptions = subscriptions
                        .Union(LoadSubscriptionsFromServer(currentEnvironment, currentMode, ref credentials)).ToList();
                }

                if (subscriptions.Any())
                {
                    return subscriptions;
                }
                else
                {
                    return new AzureSubscription[0];
                }
            }
        }

        private IList<AzureSubscription> LoadSubscriptionsFromServer(AzureEnvironment environment, AzureModule currentMode,
            ref UserCredentials credentials)
        {
            IList<AzureSubscription> result;
            if (currentMode == AzureModule.AzureResourceManager)
            {
                result = MergeSubscriptionsFromServer(GetServiceManagementSubscriptions(environment, ref credentials).ToList(),
                    GetResourceManagerSubscriptions(environment, ref credentials).ToList());
            }
            else
            {
                result = MergeSubscriptionsFromServer(GetServiceManagementSubscriptions(environment, ref credentials).ToList(),
                    null);
            }

            // Set user ID
            foreach (var subscription in result)
            {
                subscription.Properties[AzureSubscription.Property.UserAccount] = credentials.UserName;
            }

            return result;
        }

        private IList<AzureSubscription> MergeSubscriptionsFromServer(IList<AzureSubscription> serviceManagementSubscriptions,
            IList<AzureSubscription> resourceManagementSubscriptions)
        {
            if (serviceManagementSubscriptions == null)
            {
                serviceManagementSubscriptions = new List<AzureSubscription>();
            }
            if (resourceManagementSubscriptions == null)
            {
                resourceManagementSubscriptions = new List<AzureSubscription>();
            }
            serviceManagementSubscriptions.ForEach(s => s.Properties[AzureSubscription.Property.AzureMode] = AzureModule.AzureServiceManagement.ToString());
            resourceManagementSubscriptions.ForEach(s => s.Properties[AzureSubscription.Property.AzureMode] = AzureModule.AzureResourceManager.ToString());

            IList<AzureSubscription> mergedSubscriptions = new List<AzureSubscription>(serviceManagementSubscriptions);
            foreach (var csmSubscription in resourceManagementSubscriptions)
            {
                var rdfeSubscription = mergedSubscriptions.FirstOrDefault(s => s.Id == csmSubscription.Id);
                if (rdfeSubscription != null)
                {
                    rdfeSubscription.Properties[AzureSubscription.Property.AzureMode] =
                        AzureModule.AzureServiceManagement + "," + AzureModule.AzureResourceManager;
                }
                else
                {
                    mergedSubscriptions.Add(csmSubscription);
                }
            }
            return mergedSubscriptions;
        }

        private IEnumerable<AzureSubscription> GetResourceManagerSubscriptions(AzureEnvironment environment, ref UserCredentials credentials)
        {
            IAccessToken commonTenantToken = AzureSession.AuthenticationFactory.Authenticate(environment, ref credentials);

            List<AzureSubscription> result = new List<AzureSubscription>();
            TenantListResult tenants;
            using (var subscriptionClient = AzureSession.ClientFactory.CreateClient<Azure.Subscriptions.SubscriptionClient>(
                new TokenCloudCredentials(commonTenantToken.AccessToken),
                environment.GetEndpoint(AzureEnvironment.Endpoint.ResourceManagerEndpoint)))
            {
                tenants = subscriptionClient.Tenants.List();
            }

            // Go over each tenant and get all subscriptions for tenant
            foreach (var tenant in tenants.TenantIds)
            {
                // Generate tenant specific token to query list of subscriptions
                IAccessToken tenantToken = AzureSession.AuthenticationFactory.Authenticate(environment, tenant.TenantId, ref credentials);

                using (var subscriptionClient = AzureSession.ClientFactory.CreateClient<Azure.Subscriptions.SubscriptionClient>(
                        new TokenCloudCredentials(tenantToken.AccessToken),
                        environment.GetEndpoint(AzureEnvironment.Endpoint.ResourceManagerEndpoint)))
                {
                    var subscriptionListResult = subscriptionClient.Subscriptions.List();
                    foreach (var subscription in subscriptionListResult.Subscriptions)
                    {
                        AzureSubscription psSubscription = new AzureSubscription
                        {
                            Id = new Guid(subscription.SubscriptionId),
                            Name = subscription.DisplayName,
                            Environment = environment.Name
                        };
                        if (commonTenantToken.LoginType == LoginType.LiveId)
                        {
                            AzureSession.SubscriptionTokenCache[psSubscription.Id] = tenantToken;
                        }
                        else
                        {
                            AzureSession.SubscriptionTokenCache[psSubscription.Id] = commonTenantToken;
                        }

                        result.Add(psSubscription);
                    }
                }
            }

            return result;
        }

        private IEnumerable<AzureSubscription> GetServiceManagementSubscriptions(AzureEnvironment environment, ref UserCredentials credentials)
        {
            IAccessToken commonTenantToken = AzureSession.AuthenticationFactory.Authenticate(environment, ref credentials);

            List<AzureSubscription> result = new List<AzureSubscription>();
            using (var subscriptionClient = AzureSession.ClientFactory.CreateClient<WindowsAzure.Subscriptions.SubscriptionClient>(
                        new TokenCloudCredentials(commonTenantToken.AccessToken),
                        environment.GetEndpoint(AzureEnvironment.Endpoint.ServiceEndpoint)))
            {
                var subscriptionListResult = subscriptionClient.Subscriptions.List();
                foreach (var subscription in subscriptionListResult.Subscriptions)
                {
                    AzureSubscription psSubscription = new AzureSubscription
                    {
                        Id = new Guid(subscription.SubscriptionId),
                        Name = subscription.SubscriptionName,
                        Environment = environment.Name
                    };
                    if (commonTenantToken.LoginType == LoginType.LiveId)
                    {
                        AzureSession.SubscriptionTokenCache[psSubscription.Id] = 
                            AzureSession.AuthenticationFactory.Authenticate(environment, 
                            subscription.ActiveDirectoryTenantId, ref credentials);
                    }
                    else
                    {
                        AzureSession.SubscriptionTokenCache[psSubscription.Id] = commonTenantToken;
                    }

                    result.Add(psSubscription);
                }
            }

            return result;
        }

        public void AddSubscriptions(IEnumerable<AzureSubscription> subscriptions)
        {
            foreach (var subscription in subscriptions)
            {
                Profile.Subscriptions[subscription.Id] = subscription;
            }
        }
    }
}