using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.IO.Pipes;

namespace MuseLab
{
    public class DiscordIpcReader
    {
        public void Start()
        {
            try
            {
                using (var pipe = new NamedPipeClientStream(".", "discord-ipc-0", PipeDirection.InOut))
                {
                    pipe.Connect();

                    Console.WriteLine("Connected to Discord IPC");

                    while (true)
                    {
                        byte[] header = new byte[8];
                        pipe.Read(header, 0, 8);

                        int length = BitConverter.ToInt32(header, 4);

                        byte[] data = new byte[length];
                        pipe.Read(data, 0, length);

                        string json = Encoding.UTF8.GetString(data);

                        // Muse Dash만 필터링
                        if (json.Contains("Muse Dash"))
                        {
                            Console.WriteLine("뮤즈대시 감지!");

                            try
                            {
                                using (var doc = System.Text.Json.JsonDocument.Parse(json))
                                {
                                    var root = doc.RootElement;

                                    if (root.TryGetProperty("data", out var dataObj))
                                    {
                                        if (dataObj.TryGetProperty("details", out var details))
                                        {
                                            Console.WriteLine("곡명: " + details.GetString());
                                        }

                                        if (dataObj.TryGetProperty("state", out var state))
                                        {
                                            Console.WriteLine("난이도: " + state.GetString());
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }
    }
}