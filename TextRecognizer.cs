using System.Diagnostics;
using Vosk;

internal class TextRecognizer
{
	private Process cmd = null!;
	private VoskRecognizer vosk = null!;

	public TextRecognizer(VoskRecognizer vosk)
	{
		this.vosk = vosk;
	}

	public void ClearRam()
	{
		using (cmd = new Process())
		{
			cmd.StartInfo.FileName = "bash";
			cmd.StartInfo.Arguments = "~/clearcache.sh";
			cmd.StartInfo.UseShellExecute = false;
			cmd.StartInfo.CreateNoWindow = true;
			cmd.Start();
		}
	}

	public async Task ConvertToWav(string filePath)
	{
		using (cmd = new Process())
		{
			cmd.StartInfo.FileName = "ffmpeg";
			cmd.StartInfo.Arguments =
			$"-i \"{filePath}\" -nostats -loglevel 0 -acodec pcm_s16le -ac 1 -ar 16000 -y \"{filePath.Replace(".ogg", ".wav")}\"";
			cmd.StartInfo.UseShellExecute = false;
			cmd.StartInfo.CreateNoWindow = true;
			cmd.Start();
			await cmd.WaitForExitAsync();
		}
	}

	public string VoiceToTextRecognizing(string filePath)
	{
		using (var voiceMessageFile = File.OpenRead(filePath.Replace(".ogg", ".wav")))
		{
			byte[] buffer = new byte[4096];
			int bytesRead;

			while ((bytesRead = voiceMessageFile.Read(buffer, 0, buffer.Length)) > 0)
			{
				vosk.AcceptWaveform(buffer, bytesRead);
			}
			return vosk.Result();
		}
	}

	public string ImgToTextRecognizing(string filePath)
	{
		using (cmd = new Process())
		{
			cmd.StartInfo.FileName = "tesseract";
			cmd.StartInfo.Arguments = $"{filePath} stdout -l rus";
			cmd.StartInfo.UseShellExecute = false;
			cmd.StartInfo.CreateNoWindow = true;
			cmd.StartInfo.RedirectStandardOutput = true;
			cmd.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
			cmd.Start();

			return cmd.StandardOutput.ReadToEnd();
		}
	}
}