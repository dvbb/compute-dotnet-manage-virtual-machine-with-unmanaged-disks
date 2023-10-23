// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager.Resources.Models;
using Azure.ResourceManager.Samples.Common;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager;
using System.Net;
using Azure.ResourceManager.Network.Models;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Compute;
using System.Net.NetworkInformation;
using System.Xml.Linq;

namespace ManageVirtualMachineWithUnmanagedDisks
{
    public class Program
    {
        private static ResourceIdentifier? _resourceGroupId = null;

        /**
         * Azure Compute sample for managing virtual machines -
         *  - Create a virtual machine
         *  - Start a virtual machine
         *  - Stop a virtual machine
         *  - Restart a virtual machine
         *  - Update a virtual machine
         *    - Expand the OS drive
         *    - Tag a virtual machine (there are many possible variations here)
         *    - Attach data disks
         *    - Detach data disks
         *  - List virtual machines
         *  - Delete a virtual machine.
         */
        public static async Task RunSample(ArmClient client)
        {
            string rgName = Utilities.CreateRandomName("ComputeSampleRG");
            string vnetName = Utilities.CreateRandomName("vnet");
            string nicName = Utilities.CreateRandomName("nic");
            string windowsVMName = Utilities.CreateRandomName("windowsVM");
            string linuxVMName = Utilities.CreateRandomName("linuxVM");
            try
            {
                // Get default subscription
                SubscriptionResource subscription = await client.GetDefaultSubscriptionAsync();

                // Create a resource group in the EastUS region
                Utilities.Log($"creating resource group...");
                ArmOperation<ResourceGroupResource> rgLro = await subscription.GetResourceGroups().CreateOrUpdateAsync(WaitUntil.Completed, rgName, new ResourceGroupData(AzureLocation.EastUS));
                ResourceGroupResource resourceGroup = rgLro.Value;
                _resourceGroupId = resourceGroup.Id;
                Utilities.Log("Created a resource group with name: " + resourceGroup.Data.Name);

                //============================================================

                var startTime = DateTimeOffset.Now.UtcDateTime;

                var windowsVM = azure.VirtualMachines.Define(windowsVMName)
                        .WithRegion(Region.USEast)
                        .WithNewResourceGroup(rgName)
                        .WithNewPrimaryNetwork("10.0.0.0/28")
                        .WithPrimaryPrivateIPAddressDynamic()
                        .WithoutPrimaryPublicIPAddress()
                        .WithPopularWindowsImage(KnownWindowsVirtualMachineImage.WindowsServer2012R2Datacenter)
                        .WithAdminUsername(UserName)
                        .WithAdminPassword(Password)
                        .WithUnmanagedDisks()
                        .WithSize(VirtualMachineSizeTypes.Parse("Standard_D2a_v4"))
                        .Create();
                var endTime = DateTimeOffset.Now.UtcDateTime;

                Utilities.Log($"Created VM: took {(endTime - startTime).TotalSeconds} seconds");

                Utilities.PrintVirtualMachine(windowsVM);

                windowsVM.Update()
                        .WithTag("who-rocks", "open source")
                        .WithTag("where", "on azure")
                        .Apply();

                Utilities.Log("Tagged VM: " + windowsVM.Id);

                //=============================================================
                // Update - Attach data disks

                windowsVM.Update()
                        .WithNewUnmanagedDataDisk(10)
                        .DefineUnmanagedDataDisk(DataDiskName)
                            .WithNewVhd(20)
                            .WithCaching(CachingTypes.ReadWrite)
                            .Attach()
                        .Apply();

                Utilities.Log("Attached a new data disk" + DataDiskName + " to VM" + windowsVM.Id);
                Utilities.PrintVirtualMachine(windowsVM);

                windowsVM.Update()
                    .WithoutUnmanagedDataDisk(DataDiskName)
                    .Apply();

                Utilities.Log("Detached data disk " + DataDiskName + " from VM " + windowsVM.Id);

                //=============================================================
                // Update - Resize (expand) the data disk
                // First, deallocate the virtual machine and then proceed with resize

                Utilities.Log("De-allocating VM: " + windowsVM.Id);

                windowsVM.Deallocate();

                Utilities.Log("De-allocated VM: " + windowsVM.Id);

                var dataDisk = windowsVM.UnmanagedDataDisks[0];

                windowsVM.Update()
                            .UpdateUnmanagedDataDisk(dataDisk.Name)
                            .WithSizeInGB(30)
                            .Parent()
                        .Apply();

                //=============================================================
                // Update - Expand the OS drive size by 10 GB

                int osDiskSizeInGb = windowsVM.OSDiskSize;
                if (osDiskSizeInGb == 0)
                {
                    // Server is not returning the OS Disk size, possible bug in server
                    Utilities.Log("Server is not returning the OS disk size, possible bug in the server?");
                    Utilities.Log("Assuming that the OS disk size is 256 GB");
                    osDiskSizeInGb = 256;
                }

                windowsVM.Update()
                        .WithOSDiskSizeInGB(osDiskSizeInGb + 10)
                        .Apply();

                Utilities.Log("Expanded VM " + windowsVM.Id + "'s OS disk to " + (osDiskSizeInGb + 10));

                //=============================================================
                // Start the virtual machine

                Utilities.Log("Starting VM " + windowsVM.Id);

                windowsVM.Start();

                Utilities.Log("Started VM: " + windowsVM.Id + "; state = " + windowsVM.PowerState);

                //=============================================================
                // Restart the virtual machine

                Utilities.Log("Restarting VM: " + windowsVM.Id);

                windowsVM.Restart();

                Utilities.Log("Restarted VM: " + windowsVM.Id + "; state = " + windowsVM.PowerState);

                //=============================================================
                // Stop (powerOff) the virtual machine

                Utilities.Log("Powering OFF VM: " + windowsVM.Id);

                windowsVM.PowerOff();

                Utilities.Log("Powered OFF VM: " + windowsVM.Id + "; state = " + windowsVM.PowerState);

                // Get the network where Windows VM is hosted
                var network = windowsVM.GetPrimaryNetworkInterface().PrimaryIPConfiguration.GetNetwork();

                //=============================================================
                // Create a Linux VM in the same virtual network

                Utilities.Log("Creating a Linux VM in the network");

                var linuxVM = azure.VirtualMachines.Define(linuxVMName)
                        .WithRegion(Region.USEast)
                        .WithExistingResourceGroup(rgName)
                        .WithExistingPrimaryNetwork(network)
                        .WithSubnet("subnet1") // Referencing the default subnet name when no name specified at creation
                        .WithPrimaryPrivateIPAddressDynamic()
                        .WithoutPrimaryPublicIPAddress()
                        .WithPopularLinuxImage(KnownLinuxVirtualMachineImage.UbuntuServer16_04_Lts)
                        .WithRootUsername(UserName)
                        .WithRootPassword(Password)
                        .WithSize(VirtualMachineSizeTypes.Parse("Standard_D2a_v4"))
                        .Create();

                Utilities.Log("Created a Linux VM (in the same virtual network): " + linuxVM.Id);
                Utilities.PrintVirtualMachine(linuxVM);

                //=============================================================
                // List virtual machines in the resource group

                var resourceGroupName = windowsVM.ResourceGroupName;

                Utilities.Log("Printing list of VMs =======");

                foreach (var virtualMachine in azure.VirtualMachines.ListByResourceGroup(resourceGroupName))
                {
                    Utilities.PrintVirtualMachine(virtualMachine);
                }

                //=============================================================
                // Delete the virtual machine
                Utilities.Log("Deleting VM: " + windowsVM.Id);

                azure.VirtualMachines.DeleteById(windowsVM.Id);

                Utilities.Log("Deleted VM: " + windowsVM.Id);
            }
            finally
            {
                try
                {
                    if (_resourceGroupId is not null)
                    {
                        Utilities.Log($"Deleting Resource Group: {_resourceGroupId}");
                        await client.GetResourceGroupResource(_resourceGroupId).DeleteAsync(WaitUntil.Completed);
                        Utilities.Log($"Deleted Resource Group: {_resourceGroupId}");
                    }   
                }
                catch (Exception ex)
                {
                    Utilities.Log(ex);
                }
            }
        }

        public static async Task Main(string[] args)
        {
            try
            {
                //=================================================================
                // Authenticate
                var clientId = Environment.GetEnvironmentVariable("CLIENT_ID");
                var clientSecret = Environment.GetEnvironmentVariable("CLIENT_SECRET");
                var tenantId = Environment.GetEnvironmentVariable("TENANT_ID");
                var subscription = Environment.GetEnvironmentVariable("SUBSCRIPTION_ID");
                ClientSecretCredential credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
                ArmClient client = new ArmClient(credential, subscription);

                await RunSample(client);
            }
            catch (Exception ex)
            {
                Utilities.Log(ex);
            }
        }
    }
}