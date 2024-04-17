using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Exceptions;
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
        static string? token;
        static string voiceMessageFileName = "audio.ogg";
        static string imageFileName = "image.jpg";
        static string appLocation = AppContext.BaseDirectory;
        static TextRecognizer recognizer = null!;
        static Dictionary<string, DateTime> waitingUsers = new();
        static string[] answersTemplates =
            [
                "балаболит следующее:",
                "молвит слово вот о чём:",
                "издаёт звуки похожие на эти слова:",
                "засечный чертила мямлит что-то:"
            ];
        static Random random = new Random();
        static Process cmd = null!;
        static void Main(string[] args)
        {
            while (token == null || token == string.Empty)
            {
                Console.Write("Please enter token: ");
                token = Console.ReadLine();
            }

            TelegramBotClient telegramBot = new(token);
            telegramBot.StartReceiving(OnUpdateAsync, Error);

            recognizer = new TextRecognizer(
                new VoskRecognizer(new Model(Path.Combine(appLocation, "smodelru")), 16000f)
                );

            Console.ReadLine();
        }

        static Task Error(ITelegramBotClient client, Exception exception, CancellationToken token)
        {
            throw new NotImplementedException();
        }

        async static Task OnUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            string filePath = Path.Combine(appLocation, voiceMessageFileName);
            string imagePath = Path.Combine(appLocation, imageFileName);

            var message = update.Message;
            long? chatId = message?.Chat.Id;
            long? userId = message?.From?.Id;

            try
            {
                if (message?.Text?.StartsWith("/imgtotext") ?? false)
                {
                    if (chatId != null && userId != null)
                    {
                        if (waitingUsers.TryAdd($"{userId}{chatId}", DateTime.Now) == false)
                            waitingUsers[$"{userId}{chatId}"] = DateTime.Now;
                        await botClient.SendTextMessageAsync(chatId, "Ожидаю изображение...");
                    }
                    return;
                }

                if (waitingUsers.ContainsKey($"{userId}{chatId}"))
                {
                    if (waitingUsers.TryGetValue($"{userId}{chatId}", out DateTime time))
                    {
                        int durationMinutes = (time - DateTime.Now).Duration().Minutes;
                        if (durationMinutes >= 2)
                        {
                            waitingUsers.Remove($"{userId}{chatId}");
                            return;
                        }
                    }

                    if (message?.Photo != null && message?.Photo.Length > 0)
                    {
                        var photo = message.Photo;
                        string fileId = photo[photo.Length - 1].FileId ?? throw new NullReferenceException();
                        var file = await botClient.GetFileAsync(fileId);

                        using (var fileStream = new FileStream(imagePath, FileMode.Create))
                        {
                            await botClient.DownloadFileAsync(file.FilePath ?? throw new NullReferenceException(), fileStream);
                        }

                        string textFromImg = recognizer.ImgToTextRecognizing(imagePath);

                        if (chatId != null)
                        {
                            if (textFromImg != null && textFromImg != string.Empty)
                                await botClient.SendTextMessageAsync(chatId, textFromImg);
                        }
                        waitingUsers.Remove($"{userId}{chatId}");
                    }
                }

                if (message?.Voice != null)
                {
                    string userName = message?.From?.FirstName ?? string.Empty;
                    string fileId = message?.Voice.FileId ?? throw new NullReferenceException();

                    var file = await botClient.GetFileAsync(fileId);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await botClient.DownloadFileAsync(file.FilePath ?? throw new NullReferenceException(),
                            fileStream);
                    }

                    await recognizer.ConvertToWav(filePath);

                    var result = JsonSerializer.Deserialize<TextResult>(recognizer.VoiceToTextRecognizing(filePath));
                    if (result != null)
                    {
                        string textResult = $"{userName} {answersTemplates[random.Next(0, answersTemplates.Length)]}\n\r\n\r{result.Text}";
                        if (chatId != null && result.Text != string.Empty)
                            await botClient.SendTextMessageAsync(chatId, textResult);
                    }
                }
            }
            catch (ApiRequestException)
            {
                if (chatId != null)
                    await botClient.SendTextMessageAsync(chatId, "Не удалось распознать текст :(");
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

                if (System.IO.File.Exists(imagePath))
                    System.IO.File.Delete(imagePath);

                recognizer.ClearRam();
            }
        }
    }
}
