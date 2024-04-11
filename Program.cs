using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Telegram.Bot;
using Telegram.Bot.Types;
using Vosk;

namespace IHateVoiceMessageBot
{
    internal class TextResult
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = null!;
    }

    internal class Program
    {
        static string appLocation = null!;
        static string? token;
        static bool awaitingImage = false;
        static string voiceMessageFileName = "audio.ogg";
        static string imageFileName = "image.jpg";
        static string[] answersTemplates = 
            [
                "балаболит следующее:",
                "молвит слово вот о чём:",
                "издаёт звуки похожие на эти слова:",
                "засечный чертила мямлит что-то:"
            ];

        static Model model = null!;
        static VoskRecognizer vosk = null!;

        static Random random = new Random();
        static Process cmd = null!;

        static string textResult = null!;
        static void Main(string[] args)
        {
            appLocation = AppContext.BaseDirectory;

            model = new(Path.Combine(appLocation, "smodelru"));
            vosk = new VoskRecognizer(model, 16000f);

            while (token == null || token == string.Empty)
            {
                Console.Write("Please enter token: ");
                token = Console.ReadLine();
            }

            TelegramBotClient telegramBot = new(token);
            telegramBot.StartReceiving(OnUpdateAsync, Error);

            Console.ReadLine();
        }

        static async Task ConvertToWav(string filePath)
        {
            using (cmd = new Process())
            {
                cmd.StartInfo.FileName = "ffmpeg";
                cmd.StartInfo.Arguments = $"-i \"{filePath}\" -acodec pcm_s16le -ac 1 -ar 16000 -y \"{filePath.Replace(".ogg", ".wav")}\"";
                cmd.StartInfo.UseShellExecute = false;
                cmd.StartInfo.CreateNoWindow = true;
                cmd.Start();
                await cmd.WaitForExitAsync();
            }
        }

        static string TextRecognising(string filePath)
        {
            using (var voiceMessageFile = System.IO.File.OpenRead(filePath.Replace(".ogg", ".wav")))
            {
                byte[] buffer = new byte[4096];
                int bytesRead;

                while ((bytesRead = voiceMessageFile.Read(buffer, 0, buffer.Length)) > 0 && textResult == string.Empty)
                {
                    vosk.AcceptWaveform(buffer, bytesRead);
                }

                return vosk.Result();
            }
        }

        static Task Error(ITelegramBotClient client, Exception exception, CancellationToken token)
        {
            throw new NotImplementedException();
        }

        async static Task OnUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            string filePath = Path.Combine(appLocation, voiceMessageFileName);
            textResult = string.Empty;

            try
            {
                var message = update.Message;
                long? chatId = message?.Chat.Id;

                if (message?.Voice != null)
                {
                    string userName = message?.From?.FirstName ?? string.Empty;

                    string fileId = message?.Voice.FileId ?? throw new NullReferenceException();

                    var file = await botClient.GetFileAsync(fileId);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await botClient.DownloadFileAsync(file.FilePath ?? throw new NullReferenceException(), fileStream);
                    }

                    await ConvertToWav(filePath);

                    var result = JsonSerializer.Deserialize<TextResult>(TextRecognising(filePath));
                    if (result != null)
                    {
                        textResult = $"{userName} {answersTemplates[random.Next(0, answersTemplates.Length)]}\n\r\n\r{result.Text}";
                        if (chatId != null && result.Text != string.Empty)
                            await botClient.SendTextMessageAsync(chatId, textResult);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
            finally
            {
                if (System.IO.File.Exists(filePath))
                    System.IO.File.Delete(filePath);

                if (System.IO.File.Exists(filePath.Replace(".ogg", ".wav")))
                    System.IO.File.Delete(filePath.Replace(".ogg", ".wav"));
            }
        }
    }
}
