using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Diagnostics;

namespace MuseLab
{
    public class DiscordIpcReader
    {
        private CancellationTokenSource? _cts;

        public void Start(CancellationToken cancellationToken = default)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                using (var pipe = new NamedPipeClientStream(".", "discord-ipc-0", PipeDirection.InOut))
                {
                    pipe.Connect();

                    Console.WriteLine("Connected to Discord IPC");

                    while (!_cts.Token.IsCancellationRequested)
                    {
                        byte[] header = new byte[8];
                        ReadExactly(pipe, header, 0, 8);

                        int length = BitConverter.ToInt32(header, 4);

                        byte[] data = new byte[length];
                        ReadExactly(pipe, data, 0, length);

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
                            catch (System.Text.Json.JsonException ex)
                            {
                                Debug.WriteLine($"JSON parse error: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 정상 종료
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Discord IPC Error: {ex.Message}");
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
        }

        private static int ReadExactly(Stream stream, byte[] buffer, int offset, int count)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = stream.Read(buffer, offset + totalRead, count - totalRead);
                if (read == 0) throw new EndOfStreamException();
                totalRead += read;
            }
            return totalRead;
        }
    }
}