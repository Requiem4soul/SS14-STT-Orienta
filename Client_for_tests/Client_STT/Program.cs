using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace STTClient
{
    class Program
    {
        private static async Task Main(string[] args)
        {
            string serverUri = "ws://localhost:5000"; // URL сервера

            using (ClientWebSocket client = new ClientWebSocket())
            {
                Console.WriteLine("Connecting to server...");
                await client.ConnectAsync(new Uri(serverUri), CancellationToken.None);
                Console.WriteLine("Connected.");

                while (true)
                {
                    Console.WriteLine("Press 'r' to record audio or type 'exit' to quit:");
                    string command = Console.ReadLine();

                    if (command.ToLower() == "exit")
                    {
                        break;
                    }
                    else if (command.ToLower() == "r")
                    {
                        // Генерация уникального имени файла с использованием метки времени
                        string audioFilePath = $"audio_{Guid.NewGuid()}.wav";
                        Console.WriteLine($"Recording... Press Enter to stop. Saving to {audioFilePath}");

                        // Запись аудио с микрофона
                        await Task.Run(() => RecordAudio(audioFilePath));

                        // Небольшая задержка для завершения записи
                        await Task.Delay(100);

                        Console.WriteLine("Sending audio to server...");

                        // Отправка аудиофайла на сервер
                        await SendAudioFile(client, audioFilePath);

                        // Получение текста от сервера
                        string receivedText = await ReceiveTextFromServer(client);
                        Console.WriteLine($"Received transcription: {receivedText}");

                        // Удаление аудиофайла после отправки
                        if (File.Exists(audioFilePath))
                        {
                            File.Delete(audioFilePath);
                            Console.WriteLine($"Deleted file: {audioFilePath}");
                        }
                    }
                }

                await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closing", CancellationToken.None);
            }
        }

        // Функция для записи аудио
        private static void RecordAudio(string filePath)
        {
            using (var waveIn = new WaveInEvent())
            {
                waveIn.WaveFormat = new WaveFormat(16000, 1); // 16 kHz, моно
                var writer = new WaveFileWriter(filePath, waveIn.WaveFormat);

                waveIn.DataAvailable += (sender, args) =>
                {
                    writer.Write(args.Buffer, 0, args.BytesRecorded);
                };

                waveIn.RecordingStopped += (sender, args) =>
                {
                    writer.Dispose(); // Закрытие writer здесь
                };

                waveIn.StartRecording();
                Console.ReadLine(); // Ожидание нажатия клавиши для остановки записи
                waveIn.StopRecording();
            }
        }

        // Функция отправки аудиофайла на сервер
        private static async Task SendAudioFile(ClientWebSocket client, string filePath)
        {
            byte[] audioData = File.ReadAllBytes(filePath);
            var audioSegment = new ArraySegment<byte>(audioData);
            await client.SendAsync(audioSegment, WebSocketMessageType.Binary, true, CancellationToken.None);
        }

        // Функция получения текста от сервера
        private static async Task<string> ReceiveTextFromServer(ClientWebSocket client)
        {
            var buffer = new byte[1024 * 4];
            WebSocketReceiveResult result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            string receivedText = Encoding.UTF8.GetString(buffer, 0, result.Count);
            return receivedText;
        }
    }
}
