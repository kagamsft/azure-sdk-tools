﻿# ----------------------------------------------------------------------------------
#
# Copyright Microsoft Corporation
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
# http://www.apache.org/licenses/LICENSE-2.0
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.
# ----------------------------------------------------------------------------------

$randSuffix = Get-Random -Minimum 100 -Maximum 99999;
<#
.SYNOPSIS
Gets the values of the parameters used at the auditing tests
#>
function Get-SqlAuditingTestEnvironmentParameters 
{
	return @{ rgname = "sql-audit-cmdlet-test-rg" +$randSuffix;
			  serverName = "sql-audit-cmdlet-server" +$randSuffix;
			  databaseName = "sql-audit-cmdlet-db" + $randSuffix;
			  storageAccount = "auditcmdlets" +$randSuffix
			  }
}

<#
.SYNOPSIS
Creates the test environment needed to perform the Sql auditing tests
#>
function Create-TestEnvironment 
{
	$params = Get-SqlAuditingTestEnvironmentParameters
	New-AzureStorageAccount -StorageAccountName $params.storageAccount -Location "West US" 
	New-AzureResourceGroup -Name $params.rgname -Location "West US" -TemplateFile ".\Templates\sql-audit-test-env-setup.json" -serverName $params.serverName -databaseName $params.databaseName -EnvLocation "West US" -Force
}

<#
.SYNOPSIS
Removes the test environment that was needed to perform the Sql auditing tests
#>
function Remove-TestEnvironment 
{
	$params = Get-SqlAuditingTestEnvironmentParameters
	Remove-AzureResourceGroup -Name $params.rgname -force
	Remove-AzureStorageAccount -StorageAccountName $params.storageAccount
}