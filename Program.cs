namespace TestTwinsModule
{
    using System;
    using System.IO;
    using System.Collections.Generic;
    using System.Runtime.InteropServices;
    using System.Runtime.Loader;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Client.Transport.Mqtt;
    using Microsoft.Azure.Devices.Shared;
    using Newtonsoft.Json;

    class Program
    {
        static int counter;

        static void Main(string[] args)
        {
            // The Edge runtime gives us the connection string we need -- it is injected as an environment variable
            string connectionString = Environment.GetEnvironmentVariable("EdgeHubConnectionString");
            string edgeGwConnectionString = Environment.GetEnvironmentVariable("EdgeHubGwDeviceConnectionString");

            // Cert verification is not yet fully functional when using Windows OS for the container
            bool bypassCertVerification = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            if (!bypassCertVerification) InstallCert();
            Init(connectionString, edgeGwConnectionString, bypassCertVerification).Wait();

            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
            WhenCancelled(cts.Token).Wait();
        }

        /// <summary>
        /// Handles cleanup operations when app is cancelled or unloads
        /// </summary>
        public static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        /// <summary>
        /// Add certificate in local cert store for use by client for secure connection to IoT Edge runtime
        /// </summary>
        static void InstallCert()
        {
            string certPath = Environment.GetEnvironmentVariable("EdgeModuleCACertificateFile");
            if (string.IsNullOrWhiteSpace(certPath))
            {
                // We cannot proceed further without a proper cert file
                Console.WriteLine($"Missing path to certificate collection file: {certPath}");
                throw new InvalidOperationException("Missing path to certificate file.");
            }
            else if (!File.Exists(certPath))
            {
                // We cannot proceed further without a proper cert file
                Console.WriteLine($"Missing path to certificate collection file: {certPath}");
                throw new InvalidOperationException("Missing certificate file.");
            }
            X509Store store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(new X509Certificate2(X509Certificate2.CreateFromCertFile(certPath)));
            Console.WriteLine("Added Cert: " + certPath);
            store.Close();
        }


        /// <summary>
        /// Initializes the DeviceClient and sets up the callback to receive
        /// messages containing temperature information
        /// </summary>
        static async Task Init(string connectionString, string gwConnectionString, bool bypassCertVerification = false)
        {
            Console.WriteLine("Module Connection String {0}", connectionString);
            Console.WriteLine("Edge Gateway Connection String {0}", gwConnectionString);

            MqttTransportSettings mqttSetting = new MqttTransportSettings(TransportType.Mqtt_Tcp_Only);
            // During dev you might want to bypass the cert verification. It is highly recommended to verify certs systematically in production
            if (bypassCertVerification)
            {
                mqttSetting.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
            }
            ITransportSettings[] settings = { mqttSetting };

            // Open a connection to the Edge runtime
            DeviceClient ioTHubModuleClient = DeviceClient.CreateFromConnectionString(connectionString, settings);
            await ioTHubModuleClient.OpenAsync();
            Console.WriteLine("IoT Hub module client initialized.");
            
            // Read and update the IoT Hub Module Device Twin
            System.Console.WriteLine("Reading and Writing Module Device Twin");
            await ReadAndUpdateDeviceTwin(ioTHubModuleClient);
            System.Console.WriteLine("--------------------------------------");
            System.Console.WriteLine("");

            // Read and update the IoT Hub Gateway Device Twin
            DeviceClient iotGwDeviceClient = DeviceClient.CreateFromConnectionString(gwConnectionString, settings);
            await iotGwDeviceClient.OpenAsync();
            Console.WriteLine("IoT edge gateway device client initialized.");

            // Read and update the IoT Hub Module Device Twin
            System.Console.WriteLine("Reading and Writing Gateway Device Twin");
            await ReadAndUpdateDeviceTwin(iotGwDeviceClient);
            System.Console.WriteLine("--------------------------------------");
            System.Console.WriteLine("");

            // Register callback to be called when a message is received by the module
            await ioTHubModuleClient.SetInputMessageHandlerAsync("input1", PipeMessage, ioTHubModuleClient);
        }

        private static async Task ReadAndUpdateDeviceTwin(DeviceClient deviceClientProxy)
        {
            // Getting the modules Device Twin
            var twin = default(Twin);
            try
            {
                System.Console.WriteLine("-- Try getting module twin -- ");
                twin = await deviceClientProxy.GetTwinAsync();
                Console.WriteLine("Device Twin Content:");
                System.Console.WriteLine("- Reported: ");
                System.Console.WriteLine(twin.Properties.Reported.ToJson());
                System.Console.WriteLine("- Desired: ");
                System.Console.WriteLine(twin.Properties.Desired.ToJson());
                System.Console.WriteLine("--- Done ---");
            }
            catch (Exception ex)
            {
                Console.WriteLine("--- ERROR ---");
                Console.WriteLine(ex.ToString());
                System.Console.WriteLine("--- ERROR ---");
            }

            // Try setting a reported property
            try
            {
                System.Console.WriteLine("--- Try Updating reported props ---");
                if (twin != null)
                {
                    var innerReported = new TwinCollection();
                    innerReported["date"] = DateTime.UtcNow.ToLongDateString();
                    innerReported["time"] = DateTime.UtcNow.ToLongTimeString();

                    var newReported = new TwinCollection();
                    newReported["first"] = $"First Reported Property {(new Random((int)DateTime.UtcNow.Ticks)).Next(int.MaxValue)}";
                    newReported["second"] = innerReported;

                    await deviceClientProxy.UpdateReportedPropertiesAsync(newReported);
                    System.Console.WriteLine("--- Done ---");

                    System.Console.WriteLine("--- Trying to get the twin, again ---");
                    twin = await deviceClientProxy.GetTwinAsync();
                    System.Console.WriteLine("- Reported (new): ");
                    System.Console.WriteLine(twin.Properties.Reported.ToJson());
                    System.Console.WriteLine("--- Done ---");

                }
                else
                {
                    System.Console.WriteLine("--- No device twin present, maybe caused by previous error ---");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("--- ERROR ---");
                Console.WriteLine(ex.ToString());
                System.Console.WriteLine("--- ERROR ---");
            }
        }

        /// <summary>
        /// This method is called whenever the module is sent a message from the EdgeHub. 
        /// It just pipe the messages without any change.
        /// It prints all the incoming messages.
        /// </summary>
        static async Task<MessageResponse> PipeMessage(Message message, object userContext)
        {
            int counterValue = Interlocked.Increment(ref counter);

            var deviceClient = userContext as DeviceClient;
            if (deviceClient == null)
            {
                throw new InvalidOperationException("UserContext doesn't contain " + "expected values");
            }

            byte[] messageBytes = message.GetBytes();
            string messageString = Encoding.UTF8.GetString(messageBytes);
            Console.WriteLine($"Received message: {counterValue}, Body: [{messageString}]");

            if (!string.IsNullOrEmpty(messageString))
            {
                var pipeMessage = new Message(messageBytes);
                foreach (var prop in message.Properties)
                {
                    pipeMessage.Properties.Add(prop.Key, prop.Value);
                }
                await deviceClient.SendEventAsync("output1", pipeMessage);
                Console.WriteLine("Received message sent");
            }
            return MessageResponse.Completed;
        }
    }
}