using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Telegram.Bot;
using Telegram.Bot.Types;
using Vosk;
using Emgu.CV.OCR;
using Emgu.CV.Structure;
using Emgu.CV;

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
        
        static Tesseract tesseract = null!;
        static string lang = "rus+eng";

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

        static string ImgToTextRecognising(string filePath)
        {
            using (tesseract = new Tesseract(Path.Combine(appLocation, "TesseractModels"), lang, OcrEngineMode.LstmOnly))
            {
                tesseract.SetImage(new Image<Bgr, byte>(filePath));
                tesseract.Recognize();
                return $"{tesseract.GetUTF8Text()}";
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
            string imagePath = Path.Combine(appLocation, imageFileName);
            textResult = string.Empty;

            try
            {
                var message = update.Message;
                long? chatId = message?.Chat.Id;

                if (message?.Text?.StartsWith("/imgtotext") ?? false && awaitingImage == false)
                {
                    awaitingImage = true;
                    if (chatId != null)
                        await botClient.SendTextMessageAsync(chatId, "Ожидаю изображение...");
                    return;
                }

                if (awaitingImage)
                {
                    if (message?.Photo != null && message?.Photo.Length > 0)
                    {
                        string fileId = message?.Photo[0].FileId ?? throw new NullReferenceException();
                        var file = await botClient.GetFileAsync(fileId);

                        using (var fileStream = new FileStream(imagePath, FileMode.Create))
                        {
                            await botClient.DownloadFileAsync(file.FilePath ?? throw new NullReferenceException(), fileStream);
                        }

                        string textResult = ImgToTextRecognising(imagePath);

                        if (chatId != null)
                        {
                            if (textResult != null && textResult != string.Empty)
                                await botClient.SendTextMessageAsync(chatId, textResult);
                            else
                                await botClient.SendTextMessageAsync(chatId, "Не удалось распознать текст :(");
                        }
                        awaitingImage = false;
                    }
                }

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
                        if (chatId != null && textResult != null && textResult != string.Empty)
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

                if (System.IO.File.Exists(imagePath))
                    System.IO.File.Delete(imagePath);
            }
        }
    }
}
