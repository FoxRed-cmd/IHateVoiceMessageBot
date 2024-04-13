using System.Text;
using HtmlAgilityPack;
internal class VivtSchedule
{
	public string Day { get; set; } = null!;
	public string Schedule { get; set; } = null!;

	private static List<VivtSchedule> schedules = null!;
	private static HttpClient? client;
	private static Dictionary<string, int> daysOfWeak = new()
	{
		["Понедельник"] = 1,
		["Вторник"] = 2,
		["Среда"] = 3,
		["Четверг"] = 4,
		["Пятница"] = 5,
		["Суббота"] = 6,
	};

	public static async Task<List<VivtSchedule>> GetScheduleAsync(string url, string authToken)
	{
		client = new();
		string content = null!;
		using (var httpRequest = new HttpRequestMessage(HttpMethod.Get, url))
		{
			httpRequest.Headers.Add("Accept", "application/json");
			httpRequest.Headers.Add("Cookie", $"advanced-frontend={authToken}");

			var response = await client.SendAsync(httpRequest);
			content = await response.Content.ReadAsStringAsync();
		}

		HtmlDocument document = new();
		document.LoadHtml(content);

		var table = document.DocumentNode.Descendants("table")
	.FirstOrDefault(t => t.HasClass("table")) ?? new HtmlNode(HtmlNodeType.Element, document, 0);

		var strings = table.InnerText.ReplaceLineEndings("").Split(" ").Where(i => i != "").ToList();

		StringBuilder stringBuilder = new();
		schedules = new();
		VivtSchedule timeTable = null!;

		foreach (var item in strings)
		{
			if (daysOfWeak.ContainsKey(item))
			{
				if (stringBuilder.Length > 0)
				{
					timeTable.Schedule = stringBuilder.ToString();
					stringBuilder.Clear();
					schedules.Add(timeTable);
				}
				timeTable = new()
				{
					Day = item
				};
			}
			else
			{
				if (item.Length == 1 && char.IsDigit(item[0]))
					stringBuilder.Append(Environment.NewLine + Environment.NewLine);
				else
				{
					stringBuilder.Append(item + " ");
				}
			}
		}

		timeTable.Schedule = stringBuilder.ToString();
		stringBuilder.Clear();
		schedules.Add(timeTable);

		return schedules;
	}
}