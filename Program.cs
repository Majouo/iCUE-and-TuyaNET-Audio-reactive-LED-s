using System;
using com.clusterrr.TuyaNet;
using CUESDK;
using NAudio.CoreAudioApi;

namespace Main // Note: actual namespace depends on the project name.
{
    public class API_Handler
    {
        

    }
    public class CUE_Handler:API_Handler
    {
        private int deviceCount;
        public CUE_Handler()
        {
            deviceCount = connectAPI();
        }
        CorsairLedColor[] currentColor = new CorsairLedColor[2];
        public static int connectAPI()// find corsair devices
        {
            CorsairLightingSDK.PerformProtocolHandshake();
            if (CorsairLightingSDK.GetLastError() != CorsairError.Success)
            {
                return 0;
            }
            CorsairLightingSDK.RequestControl(CorsairAccessMode.ExclusiveLightingControl);
            int Count = CorsairLightingSDK.GetDeviceCount();
            return Count;
        }

        public async Task<bool> ColourManagementAsync(Tuya_Handler tuya_Handler)//interprete audio master volume for brightness of corsair and tuya devices
        {
 
            await tuya_Handler.ScanKEY();
            foreach (var device in tuya_Handler.bulbs)
            {
                tuya_Handler.SetScene(device.Ip, device.LocalKey, device.Id, "white");
            }

            int prevVolume = 255;
            int currVolume = prevVolume;
            CorsairLedColor[] currentColor = new CorsairLedColor[2];

            //Color pick
            currentColor[0].R = 0;
            currentColor[0].G = 251;
            currentColor[0].B = 48;
            //Color pick

            while (true)
            {
                MMDeviceEnumerator devEnum = new MMDeviceEnumerator();
                MMDevice defaultDevice = devEnum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                prevVolume = currVolume;
                currVolume = (char)(255 * defaultDevice.AudioMeterInformation.MasterPeakValue);
                Console.WriteLine(defaultDevice.AudioMeterInformation.MasterPeakValue);
                Console.WriteLine(currVolume);

                double fixedVolume = currVolume * prevVolume * (currVolume / 255.0) * (currVolume / 255.0) / 255.0; //equation for interpretation of audio
                Console.WriteLine(fixedVolume);
                currentColor[1].R = (int)((double)fixedVolume * (double)currentColor[0].R/255.0);
                currentColor[1].G = (int)((double)fixedVolume * (double)currentColor[0].G/255.0);
                currentColor[1].B = (int)((double)fixedVolume * (double)currentColor[0].B/255.0);


                Console.WriteLine($"Changing color to {{ R = {currentColor[1].R}, G = {currentColor[1].G}, B = {currentColor[1].B} }}");

                for (var i = 0; i < deviceCount; i++)
                {
                    var deviceLeds = CorsairLightingSDK.GetLedPositionsByDeviceIndex(i);
                    var buffer = new CorsairLedColor[deviceLeds.NumberOfLeds];

                    for (var j = 0; j < deviceLeds.NumberOfLeds; j++)
                    {
                        buffer[j] = currentColor[1];
                        buffer[j].LedId = deviceLeds.LedPosition[j].LedId;
                    }

                    CorsairLightingSDK.SetLedsColorsBufferByDeviceIndex(i, buffer);
                    CorsairLightingSDK.SetLedsColorsFlushBuffer();

                }

                string tuya_colour = "008803e80" + ((int)fixedVolume*3).ToString("X3");// color,contrast,brightness 
                Console.WriteLine(tuya_colour);
                foreach(var device in tuya_Handler.bulbs)
                {
                    tuya_Handler.SetColour(device.Ip, device.LocalKey, device.Id, tuya_colour);
                }

            }

            return false;
        }
    }

    public class Tuya_Handler:API_Handler
    {
        internal List<TuyaDeviceApiInfo> bulbs = new List<TuyaDeviceApiInfo>();
        public Tuya_Handler()
        {

        }

        private static void Scanner_OnNewDeviceInfoReceived(object sender, TuyaDeviceScanInfo e,ref TuyaDeviceApiInfo[] devices)
        {
            Console.WriteLine($"New device found! IP: {e.IP} ID: {e.GwId}");
            foreach(var device in devices) if(device.Id==e.GwId)
            {
                device.Ip = e.IP;
            }
        }

        internal async Task<bool> ScanKEY()//get key for local tuya devices 
        {
            var api = new TuyaApi(region: TuyaApi.Region.CentralEurope, accessId: "accesId", apiSecret: "apiSecret");
            var devices = await api.GetAllDevicesInfoAsync(anyDeviceId: "local");
            var scanner = new TuyaScanner();
            scanner.OnNewDeviceInfoReceived += (sender,e) => Scanner_OnNewDeviceInfoReceived(sender,e,ref devices);
            Console.WriteLine("Scanning local network for Tuya devices, press any key to stop.");
            scanner.Start();
            Console.ReadKey();
            scanner.Stop();
            if (devices.Length == 0)
            {
                Console.WriteLine("Cannot get any device key");
                return false;
            }
            else
            {
                foreach (var device in devices)
                {
                    Console.WriteLine($"Device: {device.Name}, device ID: {device.Id}, local key: {device.LocalKey}, ip: {device.Ip}");
                    bulbs.Add(device);
                }
                return true;
            }
            
        }

        public async Task<bool> SetScene( string device_ip, string device_key, string device_id, string scene)//sets mode for tuya bulbs
        {
            foreach (var bulb in bulbs)
            {
                var device = new TuyaDevice(ip: bulb.Ip, localKey: bulb.LocalKey, deviceId: bulb.Id);
                byte[] request = device.EncodeRequest(TuyaCommand.CONTROL, device.FillJson($"{{\"dps\":{{\"21\":\"{scene}\"}}}}"));
                device.SendAsync(request);
            }
            return true;
        }
        public async Task<bool> SetColour(string device_ip, string device_key, string device_id, string colour)//sets color for tuya bulbs
        {
            foreach (var bulb in bulbs)
            {
                var device = new TuyaDevice(ip: bulb.Ip, localKey: bulb.LocalKey, deviceId: bulb.Id);
                byte[] request = device.EncodeRequest(TuyaCommand.CONTROL, device.FillJson($"{{\"dps\":{{\"24\":\"{colour}\"}}}}"));
                device.SendAsync(request);
            }
            return true;
        }




    }

    internal class Program
    {
        static async Task Main()
        {
            Tuya_Handler tuya_Handler = new Tuya_Handler();
            CUE_Handler cue_Handler = new CUE_Handler();
            await cue_Handler.ColourManagementAsync(tuya_Handler);




        }

    }
}
