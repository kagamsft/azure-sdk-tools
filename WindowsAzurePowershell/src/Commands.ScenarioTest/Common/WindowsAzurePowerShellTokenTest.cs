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


using Microsoft.WindowsAzure.Commands.Test.Utilities.Common;

namespace Microsoft.WindowsAzure.Commands.ScenarioTest.Common
{
    using System;
    using System.Collections.Generic;
    using VisualStudio.TestTools.UnitTesting;
    using Commands.Common;
    using Microsoft.WindowsAzure.Utilities.HttpRecorder;
    using Microsoft.WindowsAzure.Commands.Utilities.Common;

    [TestClass]
    public class WindowsAzurePowerShellTokenTest : PowerShellTest
    {
        protected List<HttpMockServer> mockServers;

        private void OnClientCreated(object sender, ClientCreatedArgs e)
        {
            HttpMockServer mockServer = new HttpMockServer(new SimpleRecordMatcher());
            e.AddHandlerToClient(mockServer);
            mockServers.Add(mockServer);
        }

        public WindowsAzurePowerShellTokenTest(params string[] modules)
            : base(modules)
        {
            RDFETestEnvironmentFactory rdfeTestEnvironmentFactory = new RDFETestEnvironmentFactory();
            TestEnvironment rdfeEnvironment = rdfeTestEnvironmentFactory.GetTestEnvironment();
            CSMTestEnvironmentFactory csmTestEnvironmentFactory = new CSMTestEnvironmentFactory();
            TestEnvironment csmEnvironment = csmTestEnvironmentFactory.GetTestEnvironment();
            string jwtToken = ((TokenCloudCredentials)csmEnvironment.Credentials).Token;

            WindowsAzureProfile.Instance.TokenProvider = new FakeAccessTokenProvider(jwtToken, csmEnvironment.UserName);
            WindowsAzureProfile.Instance.CurrentEnvironment.ActiveDirectoryEndpoint =
                csmEnvironment.GalleryUri.AbsoluteUri;
            WindowsAzureProfile.Instance.CurrentEnvironment.ResourceManagerEndpoint =
                csmEnvironment.BaseUri.AbsoluteUri;
            WindowsAzureProfile.Instance.CurrentEnvironment.ServiceEndpoint =
                rdfeEnvironment.BaseUri.AbsoluteUri;
        }

        [TestInitialize]
        public override void TestSetup()
        {
            base.TestSetup();
            this.mockServers = new List<HttpMockServer>();
            WindowsAzureSubscription.OnClientCreated += OnClientCreated;
            System.Net.ServicePointManager.ServerCertificateValidationCallback += (se, cert, chain, sslerror) =>
            {
                return true;
            };
            RunPowerShellTest("Add-AzureAccount");
            foreach (var subscription in WindowsAzureProfile.Instance.Subscriptions)
            {
                subscription.TokenProvider = WindowsAzureProfile.Instance.TokenProvider;
                if (subscription.ActiveDirectoryUserId == null)
                {
                    subscription.IsDefault = false;
                }
                else
                {
                    subscription.IsDefault = true;
                }
            }
        }

        [TestCleanup]
        public override void TestCleanup()
        {
            base.TestCleanup();
            WindowsAzureSubscription.OnClientCreated -= OnClientCreated;
            mockServers.ForEach(ms => ms.Dispose());
        }
    }
}