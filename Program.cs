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
        static readonly Dictionary<string, DateTime> waitingUsers = [];
        static string[] answersTemplates =
            [
                "балаболит следующее:",
                "молвит слово вот о чём:",
                "издаёт звуки похожие на эти слова:",
                "засечный чертила мямлит что-то:"
            ];
        static readonly Random random = new();
        static void Main(string[] args)
        {
            while (string.IsNullOrEmpty(token))
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

        async static Task AnswerForPhoto(ITelegramBotClient botClient, string imagePath,
            PhotoSize[] photo, long chatId, long? userId, CancellationToken token)
        {
            string fileId = photo[^1].FileId ?? throw new NullReferenceException();
            var file = await botClient.GetFileAsync(fileId, token);

            using (var fileStream = new FileStream(imagePath, FileMode.Create))
            {
                await botClient.DownloadFileAsync(
                    file.FilePath ?? throw new NullReferenceException(),
                    fileStream, token);
            }

            string textFromImg = recognizer.ImgToTextRecognizing(imagePath);

            if (textFromImg != null && textFromImg != string.Empty)
                await botClient.SendTextMessageAsync(
                    chatId, textFromImg, cancellationToken: token);
            waitingUsers.Remove($"{userId}{chatId}");
        }

        async static Task AnswerForVoice(ITelegramBotClient botClient, string filePath,
            string fileId, long chatId, string userName, CancellationToken token)
        {
            var file = await botClient.GetFileAsync(
                        fileId, cancellationToken: token);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await botClient.DownloadFileAsync(file.FilePath ??
                    throw new NullReferenceException(),
                    fileStream, token);
            }

            await recognizer.ConvertToWav(filePath);

            var result = JsonSerializer.Deserialize<TextResult>(recognizer.VoiceToTextRecognizing(filePath));
            if (result != null)
            {
                string textResult =
                    $"{userName} {answersTemplates[random.Next(0, answersTemplates.Length)]}\n\r\n\r{result.Text}";
                if (!string.IsNullOrEmpty(result.Text))
                    await botClient.SendTextMessageAsync(
                        chatId, textResult, cancellationToken: token);
            }
        }

        static Task Error(ITelegramBotClient client, Exception exception, CancellationToken token)
        {
            throw new NotImplementedException();
        }

        async static Task OnUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            var message = update.Message ??
                throw new NullReferenceException("Message should not be null");
            long chatId = message?.Chat.Id ??
                throw new NullReferenceException("chatId should not be null");
            long? userId = message?.From?.Id ??
                throw new NullReferenceException("userId should not be null");

            string filePath = Path.Combine(appLocation, voiceMessageFileName);
            string imagePath = Path.Combine(appLocation, imageFileName);

            try
            {
                if (message.Text?.StartsWith("/imgtotext") ?? false)
                {
                    if (waitingUsers.TryAdd($"{userId}{chatId}", DateTime.Now) == false)
                        waitingUsers[$"{userId}{chatId}"] = DateTime.Now;
                    await botClient.SendTextMessageAsync(
                        chatId, "Ожидаю изображение...", cancellationToken: cancellationToken);
                    return;
                }

                if (waitingUsers.TryGetValue($"{userId}{chatId}", out DateTime time))
                {
                    int durationMinutes = (time - DateTime.Now).Duration().Minutes;
                    if (durationMinutes >= 2)
                        waitingUsers.Remove($"{userId}{chatId}");
                    else if (message.Photo != null && message.Photo.Length > 0)
                        await AnswerForPhoto(
                            botClient, imagePath, message.Photo, chatId, userId, cancellationToken);
                }

                if (message.Voice != null)
                {
                    string userName = message.From?.FirstName ?? string.Empty;
                    string fileId = message.Voice.FileId ?? throw new NullReferenceException();

                    await AnswerForVoice(
                        botClient, filePath, fileId, chatId, userName, cancellationToken);
                }
            }
            catch (ApiRequestException)
            {
                await botClient.SendTextMessageAsync(
                    chatId, "Не удалось распознать текст :(", cancellationToken: cancellationToken);
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
