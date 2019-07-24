<Query Kind="Program">
  <Reference>&lt;RuntimeDirectory&gt;\System.Web.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.Configuration.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.DirectoryServices.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.EnterpriseServices.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.Web.RegularExpressions.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.Design.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.Web.ApplicationServices.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.ComponentModel.DataAnnotations.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.DirectoryServices.Protocols.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.Security.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.ServiceProcess.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.Web.Services.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\Microsoft.Build.Utilities.v4.0.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.Runtime.Caching.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\Microsoft.Build.Framework.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\Microsoft.Build.Tasks.v4.0.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.Windows.Forms.dll</Reference>
  <NuGetReference>MediaInfo.DotNetWrapper</NuGetReference>
  <NuGetReference>Newtonsoft.Json</NuGetReference>
  <Namespace>MediaInfo.DotNetWrapper</Namespace>
  <Namespace>MediaInfo.DotNetWrapper.Enumerations</Namespace>
  <Namespace>Newtonsoft.Json</Namespace>
  <Namespace>System.Globalization</Namespace>
  <Namespace>System.Net</Namespace>
  <Namespace>System.Security.Cryptography</Namespace>
  <Namespace>System.Web</Namespace>
</Query>

#define LOCAL
#undef LOCAL
void Main()
{
	var videoDirectory = @"<path to store videos>";
	#region Local
#if (LOCAL)
	var dataSet = Directory.GetFiles(videoDirectory, "*.json").Select(x => File.ReadAllText(x)).Select(json => JsonConvert.DeserializeObject<Welcome>(json));
	var data = dataSet.SelectMany(p => p.Results);
#endif
	#endregion
	#region Web	
#if (!LOCAL)
	var requestUrl = "https://api.twitter.com/1.1/tweets/search/fullarchive/dev.json";
	var consumerKey = "<your customer key>";
	var consumerSecret = "<your customer secret>";
	var tokenValue = "<your token>";
	var tokenSecret = "<your token secret>";
	var fromDate = "200603210000"; // Minimun date allowed by this API
	var query = "#yourHashTag has:videos"; // Note change you hashtag here.

	var body = new
	{
		query = query,
		fromDate = fromDate,
		next = default(string)
	};
	var pages = new List<Welcome>();
	var counter = 0;
	var counterMax = 400; // This is so you can halt the loop early if you want.
	var now = DateTime.Now;
	while (true)
	{
		$"Fetching page {counter}".Dump();
		var page = JsonConvert.DeserializeObject<Welcome>(MakePostRequest(requestUrl, JsonConvert.SerializeObject(body, Newtonsoft.Json.Formatting.None,
							new JsonSerializerSettings
							{
								NullValueHandling = NullValueHandling.Ignore
							}), consumerKey, consumerSecret, tokenValue, tokenSecret));
		File.WriteAllText($"{videoDirectory}/data-{now.ToFileTime()}-{counter}.json", JsonConvert.SerializeObject(page)); // Saves the request locally due to twitter's stupid request limits!
		pages.Add(page);
		if (page.Next == default(string) || counter == counterMax)
			break;
		else
			body = new
			{
				query = query,
				fromDate = fromDate,
				next = page.Next
			};
		counter++;
	}
	var data = pages.SelectMany(p => p.Results);
#endif
	#endregion


	var twitterResults = data
		.Split(x => x.Count().Dump("Total Results"))
		.Where(x => x.RetweetedStatus == null)
		.Split(x => x.Count().Dump("Filtered Results"))
		.Select(r => new
		{
			Id = r.IdStr,
			Username = r.User.ScreenName,
			Name = r.User.Name,
			Text = r.Text,
			Tweet = r.ExtendedTweet?.FullText ?? r.Text,

			Date = DateTime.ParseExact(r.CreatedAt, "ddd MMM dd HH:mm:ss +0000 yyyy", CultureInfo.InvariantCulture),// Twitter's date format Tue Jan 29 03:54:53 +0000 2019
			Video = (r.ExtendedTweet?.Entities.Media ?? r.ExtendedEntities?.Media)
				.Select(m =>
				{
					var max = m.VideoInfo?.Variants.Max(v => v.Bitrate);
					var path = m.VideoInfo?.Variants.FirstOrDefault(v => v.Bitrate == max)?.Url.ToString();
					var url = (path != default(string)) ? new Uri(path) : null;
					return new { Path = path, Url = url, FileName = url?.Segments.Last() };

				})
				.Where(x => x.Path != default(string))
				// .Where(x => x.FileName != "dsz2hdR7DayXwzpN.mp4") // If you want to exclude any videos you can add .Where clauses.
				.FirstOrDefault()
		}
		).Where(x => x.Video != null).OrderBy(x => x.Date).ToList().Dump(0);
	twitterResults = twitterResults
		//.Take(10)
		.ToList();

	//var fileNames = twitterResults.Select(tw => new Uri(tw.Video)).ToList().Dump("File Names");
	var exceptions = new List<string>();

#if (!LOCAL)
	using (var client = new WebClient())
	{
		foreach (var x in twitterResults)
		{
			var fileName = x.Video.FileName;
			try
			{
				client.DownloadFile(x.Video.Url, $@"{videoDirectory}\{fileName}");
			}
			catch (WebException)
			{
				$"Couldn't download {fileName}".Dump();
				exceptions.Add(fileName);
			}
		}


	}
#endif
	exceptions.Dump("Couldn't download these files");

	var twitterResultsAugmented = twitterResults.Where(x => !exceptions.Contains(x.Video.FileName)).Select(tr => new
	{
		tr,
		hasCorrectFrameSize = NonStandardVideoSize($@"{videoDirectory}\{tr.Video.FileName}", 1280, 720), // The highest quality of videos from Twitter are 1280 x 720 (July 2019)
		hasAudio = HasAudio($@"{videoDirectory}\{tr.Video.FileName}"),
		hasCorrectWidthButIncorrectHeight = WidthMatchesButNotHeight($@"{videoDirectory}\{tr.Video.FileName}", 1280, 720) // 😲 Copy and paste!
	}).ToList();

	var commandParams = new List<string>();

	// FFMPEG does all the hardwork combining the videos together! - https://www.ffmpeg.org/
	commandParams.Add("./ffmpeg");
	var ffmpegPlaylist = $"{string.Join(" ", twitterResultsAugmented.Select(x => $@"-i '{videoDirectory}\{x.tr.Video.FileName}'"))} -f lavfi -i color=c=\"#006bb6\":s=1280x720:d=40 -f lavfi -t 0.1 -i anullsrc";
	ffmpegPlaylist.Dump("FFMPEG Paylist");
	commandParams.Add(ffmpegPlaylist);
	//-filter_complex "[0:v:0][0:a:0][1:v:0][1:a:0][2:v:0][2:a:0]concat=n=3:v=1:a=1[outv][outa]"

	Func<string, string, string, string> makeSrtText = (tweet, username, date) => $@"1
00:00:00,000 --> 00:00:08,000
{tweet}
<i>@{username} – {date}</i>";

	//$@"{videoDirectory}\{r.Video.FileName.Substring(0, r.Video.FileName.Count() - 4)}"
	twitterResultsAugmented.Select(r => new
	{
		FileName = $@"{videoDirectory}\{r.tr.Video.FileName.Substring(0, r.tr.Video.FileName.Count() - 4)}",
		TwitterResults = r.tr
	}).ToList()
	.ForEach(f => File.WriteAllText(f.FileName + ".srt",
		makeSrtText(HttpUtility.HtmlDecode(f.TwitterResults.Tweet), f.TwitterResults.Username, f.TwitterResults.Date.ToString("MMM yyyy")))
	);

	var complexFilterFile = $@"{videoDirectory}\playlist - complexFilters.txt";
	//Note maybe try this: https://superuser.com/a/1185430
	//var filter = $"-filter_complex \"{string.Join(" ", twitterResults.Select((r, i) => $"[{i}:v:0]subtitles={($@"{videoDirectory}\{r.Video.FileName.Substring(0, r.Video.FileName.Count() - 4)}").Replace(@"\", @"\\\\").Replace(":", @"\\:")}.srt[v{i}];"))} {string.Join("", Enumerable.Range(0, twitterResults.Count()).Select(i => $"[v{i}][{i}:a:0]"))}concat=n={twitterResults.Count}:v=1:a=1[outv][outa]\"";
	var filter = $"-filter_complex_script '{complexFilterFile}'";
	filter.Dump("Filter");
	commandParams.Add(filter);
	
	var abd = string.Join(" ", twitterResultsAugmented.Select((r, j) =>
	{
		var i = j + 1;
		var videoIndex = $"[{j}:v:0]";

		var padding = default(string);
		if (r.hasCorrectWidthButIncorrectHeight)
			padding = $@"scale=w=1280:h=720:force_original_aspect_ratio=1,pad=1280:720:(ow-iw)/2:(oh-ih)/2[asd]; [asd]";
		else if (!r.hasCorrectFrameSize)
			padding = $@"scale=ih*16/9:-1,boxblur=luma_radius=min(h\,w)/20:luma_power=1:chroma_radius=min(cw\,ch)/20:chroma_power=1[bg];[bg]{videoIndex}overlay=(W-w)/2:(H-h)/2,crop=h=iw*9/16[vid]; [vid]scale=1280:720[asd]; [asd]";
		var ftr = $"{videoIndex}{padding}subtitles={($@"{videoDirectory}\{r.tr.Video.FileName.Substring(0, r.tr.Video.FileName.Count() - 4)}").Replace(@"\", @"\\\\").Replace(":", @"\\:")}.srt[v{i}];";
		return ftr;
	}));

	var asg = string.Join("", Enumerable.Range(0, twitterResults.Count()).Select(j =>
	{
		var i = j + 1;
		var audio = twitterResultsAugmented.ElementAt(j).hasAudio ? $"[{j}:a:0]" : $"[{twitterResults.Count() + 1}:a]";
		return $"[v{i}]{audio}";
	}));

	var complexFilter = $"[{twitterResults.Count()}:v]ass={($@"{videoDirectory}\Title Cards.ass").Replace(@"\", @"\\\\").Replace(":", @"\\:")}[v0];{abd} [v0][{twitterResults.Count() + 1}:a]{asg}concat=n={twitterResults.Count() + 1}:v=1:a=1[outv][outa]";
	complexFilter.Dump("Complex Filters");

	var mappings = "-map \"[outv]\" -map \"[outa]\"";
	commandParams.Add(mappings);

	var codec = "-c:v libx264 -b:v 4.5M -r 60000/1001 -c:a aac -b:a 128k -ac 2";
	commandParams.Add(codec);

	var outfile = $@"""{videoDirectory}\concat.mp4""";
	commandParams.Add(outfile);

	File.WriteAllText($@"{videoDirectory}\playlist.txt", string.Join(" ", commandParams));
	File.WriteAllText(complexFilterFile, complexFilter);

}

#region MediaInfo
bool NonStandardVideoSize(string filePath, int desiredWidth, int desiredHeight)
{

	using (var mediaInfo = new MediaInfo.DotNetWrapper.MediaInfo())
	{
		mediaInfo.Open(filePath);
		var fileWidth = default(int);
		int.TryParse(mediaInfo.Get(StreamKind.Video, 0, "Width"), out fileWidth);

		if (desiredWidth != fileWidth) return false;

		var fileHeight = default(int);
		int.TryParse(mediaInfo.Get(StreamKind.Video, 0, "Height"), out fileHeight);

		if (desiredHeight != fileHeight) return false;

		return true;
	}
}

bool HasAudio(string filePath)
{
	using (var mediaInfo = new MediaInfo.DotNetWrapper.MediaInfo())
	{
		mediaInfo.Open(filePath);

		var audioStreamCount = default(int);
		int.TryParse(mediaInfo.Get(StreamKind.Audio, 0, "StreamCount"), out audioStreamCount);
		return audioStreamCount > default(int);
	}
}

bool WidthMatchesButNotHeight(string filePath, int desiredWidth, int desiredHeight)
{
	using (var mediaInfo = new MediaInfo.DotNetWrapper.MediaInfo())
	{
		mediaInfo.Open(filePath);
		var fileWidth = default(int);
		int.TryParse(mediaInfo.Get(StreamKind.Video, 0, "Width"), out fileWidth);

		if (desiredWidth != fileWidth) return false;

		var fileHeight = default(int);
		int.TryParse(mediaInfo.Get(StreamKind.Video, 0, "Height"), out fileHeight);

		if (desiredHeight != fileHeight) return true;

		return false;
	}
}
#endregion

#region Web Support

string MakePostRequest(string url, string jsonBody, string consumerKey, string consumerSecret, string tokenValue, string tokenSecret)
{
	ASCIIEncoding encoding = new ASCIIEncoding();
	byte[] bodyBytes = encoding.GetBytes(jsonBody);


	var httpWebRequest = (HttpWebRequest)WebRequest.Create(url);
	httpWebRequest.Method = "POST";

	httpWebRequest.Headers.Add(HttpRequestHeader.Authorization, GetAuthorizationHeader(httpWebRequest, consumerKey, consumerSecret, tokenValue, tokenSecret));
	httpWebRequest.ContentType = "application/json";
	httpWebRequest.UserAgent = "PostmanRuntime/7.6.0";
	httpWebRequest.Accept = "*/*";

	Stream newStream = httpWebRequest.GetRequestStream();
	newStream.Write(bodyBytes, 0, bodyBytes.Length);
	newStream.Close();

	try
	{
		var response = httpWebRequest.GetResponse();

		var characterSet = ((HttpWebResponse)response).CharacterSet;
		var responseEncoding = characterSet == string.Empty
			? Encoding.UTF8
			: Encoding.GetEncoding(characterSet ?? "utf-8");
		var responsestream = response.GetResponseStream();
		if (responsestream == null)
		{
			throw new ArgumentNullException(nameof(characterSet));
		}
		using (responsestream)
		{
			var reader = new StreamReader(responsestream, responseEncoding);
			var result = reader.ReadToEnd();
			return result;
		}
	}
	catch (WebException we)
	{
		new StreamReader(we.Response.GetResponseStream()).ReadToEnd().Dump("Web Response Error");
		httpWebRequest.Headers.Dump("Request Headers");
		throw we;
	}
}

// See: https://stackoverflow.com/questions/47378232/rest-api-authentication-oauth-1-0-using-c-sharp
string GetAuthorizationHeader(HttpWebRequest httpWebRequest, string consumerKey, string consumerSecret, string tokenValue, string tokenSecret)
{
	string EscapeUriDataStringRfc3986(string s)
	{
		// https://stackoverflow.com/questions/846487/how-to-get-uri-escapedatastring-to-comply-with-rfc-3986
		var charsToEscape = new[] { "!", "*", "'", "(", ")" };
		var escaped = new StringBuilder(Uri.EscapeDataString(s));
		foreach (var t in charsToEscape)
		{
			escaped.Replace(t, Uri.HexEscape(t[0]));
		}
		return escaped.ToString();
	}
	var timeStamp = ((int)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds).ToString();
	var nonce = EscapeUriDataStringRfc3986(Convert.ToBase64String(Encoding.UTF8.GetBytes(timeStamp)));

 	//https://tools.ietf.org/html/rfc5849#section-3.3
	//https://twittercommunity.com/t/how-to-generate-oauth-signature-for-batch-api-requests-that-have-a-json-array-parameter/64036
	var thing = new[] {
		EscapeUriDataStringRfc3986("oauth_consumer_key=" + (consumerKey)),
		EscapeUriDataStringRfc3986("&oauth_nonce=" + (nonce)),
		EscapeUriDataStringRfc3986("&oauth_signature_method=" + ("HMAC-SHA1")),
		EscapeUriDataStringRfc3986("&oauth_timestamp=" + (timeStamp)),
		EscapeUriDataStringRfc3986("&oauth_token=" + (tokenValue)),
		EscapeUriDataStringRfc3986("&oauth_version=" + "1.0")
	};
	var signatureBaseString = EscapeUriDataStringRfc3986(httpWebRequest.Method.ToUpper())
		+ "&" + EscapeUriDataStringRfc3986(httpWebRequest.Address.ToString().ToLower())
		+ "&" + string.Join("", thing);
	//	var signatureBaseString = EscapeUriDataStringRfc3986(httpWebRequest.Method.ToUpper()) + "&";
	//	signatureBaseString += EscapeUriDataStringRfc3986(httpWebRequest.Address.ToString().ToLower()) + "&";
	//	signatureBaseString += EscapeUriDataStringRfc3986(
	//		"oauth_consumer_key=" + EscapeUriDataStringRfc3986(consumerKey) + "&" +
	//		"oauth_nonce=" + EscapeUriDataStringRfc3986(nonce) + "&" +
	//		"oauth_signature_method=" + EscapeUriDataStringRfc3986("HMAC-SHA1") + "&" +
	//		"oauth_timestamp=" + EscapeUriDataStringRfc3986(timeStamp) + "&" +
	//		"oauth_token=" + EscapeUriDataStringRfc3986(tokenValue) + "&" +
	//		"oauth_version=" + EscapeUriDataStringRfc3986("1.0"));
	//Console.WriteLine(@"signatureBaseString: " + signatureBaseString);

	var key = EscapeUriDataStringRfc3986(consumerSecret) + "&" + EscapeUriDataStringRfc3986(tokenSecret);
	//Console.WriteLine(@"key: " + key);
	var signatureEncoding = new ASCIIEncoding();
	var keyBytes = signatureEncoding.GetBytes(key);
	var signatureBaseBytes = signatureEncoding.GetBytes(signatureBaseString);
	string signatureString;
	using (var hmacsha1 = new HMACSHA1(keyBytes))
	{
		var hashBytes = hmacsha1.ComputeHash(signatureBaseBytes);
		signatureString = Convert.ToBase64String(hashBytes);
	}
	signatureString = EscapeUriDataStringRfc3986(signatureString);
	//Console.WriteLine(@"signatureString: " + signatureString);

	string SimpleQuote(string s) => '"' + s + '"';
	var header = "OAuth " +
		"oauth_consumer_key=" + SimpleQuote(consumerKey) + "," +
		"oauth_token=" + SimpleQuote(tokenValue) + "," +
		"oauth_signature_method=" + SimpleQuote("HMAC-SHA1") + "," +
		"oauth_timestamp=" + SimpleQuote(timeStamp) + "," +
		"oauth_nonce=" + SimpleQuote(nonce) + "," +
		"oauth_version=" + SimpleQuote("1.0") + "," +
		"oauth_signature= " + SimpleQuote(signatureString);
	return header;
}
#endregion

#region Twitter Data Model
// Define other methods and classes here
public partial class Welcome
{
	[JsonProperty("results")]
	public List<Result> Results { get; set; }

	[JsonProperty("next")]
	public string Next { get; set; }

	[JsonProperty("requestParameters")]
	public RequestParameters RequestParameters { get; set; }
}

public partial class RequestParameters
{
	[JsonProperty("maxResults")]
	public long MaxResults { get; set; }

	[JsonProperty("fromDate")]
	public string FromDate { get; set; }

	[JsonProperty("toDate")]
	public string ToDate { get; set; }
}

public partial class Result
{
	[JsonProperty("created_at")]
	public string CreatedAt { get; set; }

	[JsonProperty("id")]
	public double Id { get; set; }

	[JsonProperty("id_str")]
	public string IdStr { get; set; }

	[JsonProperty("text")]
	public string Text { get; set; }

	[JsonProperty("display_text_range", NullValueHandling = NullValueHandling.Ignore)]
	public List<long> DisplayTextRange { get; set; }

	[JsonProperty("source")]
	public string Source { get; set; }

	[JsonProperty("truncated")]
	public bool Truncated { get; set; }

	[JsonProperty("in_reply_to_status_id")]
	public object InReplyToStatusId { get; set; }

	[JsonProperty("in_reply_to_status_id_str")]
	public object InReplyToStatusIdStr { get; set; }

	[JsonProperty("in_reply_to_user_id")]
	public object InReplyToUserId { get; set; }

	[JsonProperty("in_reply_to_user_id_str")]
	public object InReplyToUserIdStr { get; set; }

	[JsonProperty("in_reply_to_screen_name")]
	public object InReplyToScreenName { get; set; }

	[JsonProperty("user")]
	public User User { get; set; }

	[JsonProperty("geo")]
	public object Geo { get; set; }

	[JsonProperty("coordinates")]
	public object Coordinates { get; set; }

	[JsonProperty("place")]
	public Place Place { get; set; }

	[JsonProperty("contributors")]
	public object Contributors { get; set; }

	[JsonProperty("is_quote_status")]
	public bool IsQuoteStatus { get; set; }

	[JsonProperty("extended_tweet", NullValueHandling = NullValueHandling.Ignore)]
	public ExtendedTweet ExtendedTweet { get; set; }

	[JsonProperty("quote_count")]
	public long QuoteCount { get; set; }

	[JsonProperty("reply_count")]
	public long ReplyCount { get; set; }

	[JsonProperty("retweet_count")]
	public long RetweetCount { get; set; }

	[JsonProperty("favorite_count")]
	public long FavoriteCount { get; set; }

	[JsonProperty("entities")]
	public Entities Entities { get; set; }

	[JsonProperty("favorited")]
	public bool Favorited { get; set; }

	[JsonProperty("retweeted")]
	public bool Retweeted { get; set; }

	[JsonProperty("possibly_sensitive", NullValueHandling = NullValueHandling.Ignore)]
	public bool? PossiblySensitive { get; set; }

	[JsonProperty("filter_level")]
	public string FilterLevel { get; set; }

	[JsonProperty("lang")]
	public string Lang { get; set; }

	[JsonProperty("matching_rules")]
	public List<MatchingRule> MatchingRules { get; set; }

	[JsonProperty("retweeted_status", NullValueHandling = NullValueHandling.Ignore)]
	public RetweetedStatus RetweetedStatus { get; set; }

	[JsonProperty("extended_entities", NullValueHandling = NullValueHandling.Ignore)]
	public ExtendedEntities ExtendedEntities { get; set; }
}

public partial class Entities
{
	[JsonProperty("hashtags")]
	public List<Hashtag> Hashtags { get; set; }

	[JsonProperty("urls")]
	public List<Url> Urls { get; set; }

	[JsonProperty("user_mentions")]
	public List<UserMention> UserMentions { get; set; }

	[JsonProperty("symbols")]
	public List<object> Symbols { get; set; }

	[JsonProperty("media", NullValueHandling = NullValueHandling.Ignore)]
	public List<Media> Media { get; set; }
}

public partial class Hashtag
{
	[JsonProperty("text")]
	public string Text { get; set; }

	[JsonProperty("indices")]
	public List<long> Indices { get; set; }
}

public partial class Media
{
	[JsonProperty("id")]
	public double Id { get; set; }

	[JsonProperty("id_str")]
	public string IdStr { get; set; }

	[JsonProperty("indices")]
	public List<long> Indices { get; set; }

	[JsonProperty("additional_media_info")]
	public AdditionalMediaInfo AdditionalMediaInfo { get; set; }

	[JsonProperty("media_url")]
	public Uri MediaUrl { get; set; }

	[JsonProperty("media_url_https")]
	public Uri MediaUrlHttps { get; set; }

	[JsonProperty("url")]
	public Uri Url { get; set; }

	[JsonProperty("display_url")]
	public string DisplayUrl { get; set; }

	[JsonProperty("expanded_url")]
	public Uri ExpandedUrl { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("sizes")]
	public Sizes Sizes { get; set; }

	[JsonProperty("source_status_id", NullValueHandling = NullValueHandling.Ignore)]
	public double? SourceStatusId { get; set; }

	[JsonProperty("source_status_id_str", NullValueHandling = NullValueHandling.Ignore)]
	public string SourceStatusIdStr { get; set; }

	[JsonProperty("source_user_id", NullValueHandling = NullValueHandling.Ignore)]
	public long? SourceUserId { get; set; }

	[JsonProperty("source_user_id_str", NullValueHandling = NullValueHandling.Ignore)]
	public string SourceUserIdStr { get; set; }

	[JsonProperty("video_info", NullValueHandling = NullValueHandling.Ignore)]
	public VideoInfo VideoInfo { get; set; }
}

public partial class AdditionalMediaInfo
{
	[JsonProperty("monetizable")]
	public bool Monetizable { get; set; }
}

public partial class Sizes
{
	[JsonProperty("thumb")]
	public Large Thumb { get; set; }

	[JsonProperty("medium")]
	public Large Medium { get; set; }

	[JsonProperty("small")]
	public Large Small { get; set; }

	[JsonProperty("large")]
	public Large Large { get; set; }
}

public partial class Large
{
	[JsonProperty("w")]
	public long W { get; set; }

	[JsonProperty("h")]
	public long H { get; set; }

	[JsonProperty("resize")]
	public string Resize { get; set; }
}

public partial class VideoInfo
{
	[JsonProperty("aspect_ratio")]
	public List<long> AspectRatio { get; set; }

	[JsonProperty("duration_millis")]
	public long DurationMillis { get; set; }

	[JsonProperty("variants")]
	public List<Variant> Variants { get; set; }
}

public partial class Variant
{
	[JsonProperty("bitrate", NullValueHandling = NullValueHandling.Ignore)]
	public long? Bitrate { get; set; }

	[JsonProperty("content_type")]
	public Object ContentType { get; set; }

	[JsonProperty("url")]
	public Uri Url { get; set; }
}

public partial class Url
{
	[JsonProperty("url")]
	public Uri UrlUrl { get; set; }

	[JsonProperty("expanded_url")]
	public Uri ExpandedUrl { get; set; }

	[JsonProperty("display_url")]
	public string DisplayUrl { get; set; }

	[JsonProperty("indices")]
	public List<long> Indices { get; set; }
}

public partial class UserMention
{
	[JsonProperty("screen_name")]
	public string ScreenName { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("id")]
	public long? Id { get; set; }

	[JsonProperty("id_str")]
	public string IdStr { get; set; }

	[JsonProperty("indices")]
	public List<long> Indices { get; set; }
}

public partial class ExtendedEntities
{
	[JsonProperty("media")]
	public List<Media> Media { get; set; }
}

public partial class ExtendedTweet
{
	[JsonProperty("full_text")]
	public string FullText { get; set; }

	[JsonProperty("display_text_range")]
	public List<long> DisplayTextRange { get; set; }

	[JsonProperty("entities")]
	public Entities Entities { get; set; }

	[JsonProperty("extended_entities")]
	public ExtendedEntities ExtendedEntities { get; set; }
}

public partial class MatchingRule
{
	[JsonProperty("tag")]
	public object Tag { get; set; }
}

public partial class Place
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("url")]
	public Uri Url { get; set; }

	[JsonProperty("place_type")]
	public string PlaceType { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("full_name")]
	public string FullName { get; set; }

	[JsonProperty("country_code")]
	public string CountryCode { get; set; }

	[JsonProperty("country")]
	public string Country { get; set; }

	[JsonProperty("bounding_box")]
	public BoundingBox BoundingBox { get; set; }

	[JsonProperty("attributes")]
	public Attributes Attributes { get; set; }
}

public partial class Attributes
{
}

public partial class BoundingBox
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("coordinates")]
	public List<List<List<double>>> Coordinates { get; set; }
}

public partial class RetweetedStatus
{
	[JsonProperty("created_at")]
	public string CreatedAt { get; set; }

	[JsonProperty("id")]
	public double Id { get; set; }

	[JsonProperty("id_str")]
	public string IdStr { get; set; }

	[JsonProperty("text")]
	public string Text { get; set; }

	[JsonProperty("display_text_range")]
	public List<long> DisplayTextRange { get; set; }

	[JsonProperty("source")]
	public string Source { get; set; }

	[JsonProperty("truncated")]
	public bool Truncated { get; set; }

	[JsonProperty("in_reply_to_status_id")]
	public object InReplyToStatusId { get; set; }

	[JsonProperty("in_reply_to_status_id_str")]
	public object InReplyToStatusIdStr { get; set; }

	[JsonProperty("in_reply_to_user_id")]
	public object InReplyToUserId { get; set; }

	[JsonProperty("in_reply_to_user_id_str")]
	public object InReplyToUserIdStr { get; set; }

	[JsonProperty("in_reply_to_screen_name")]
	public object InReplyToScreenName { get; set; }

	[JsonProperty("user")]
	public User User { get; set; }

	[JsonProperty("geo")]
	public object Geo { get; set; }

	[JsonProperty("coordinates")]
	public object Coordinates { get; set; }

	[JsonProperty("place")]
	public Place Place { get; set; }

	[JsonProperty("contributors")]
	public object Contributors { get; set; }

	[JsonProperty("is_quote_status")]
	public bool IsQuoteStatus { get; set; }

	[JsonProperty("quote_count")]
	public long QuoteCount { get; set; }

	[JsonProperty("reply_count")]
	public long ReplyCount { get; set; }

	[JsonProperty("retweet_count")]
	public long RetweetCount { get; set; }

	[JsonProperty("favorite_count")]
	public long FavoriteCount { get; set; }

	[JsonProperty("entities")]
	public Entities Entities { get; set; }

	[JsonProperty("extended_entities")]
	public ExtendedEntities ExtendedEntities { get; set; }

	[JsonProperty("favorited")]
	public bool Favorited { get; set; }

	[JsonProperty("retweeted")]
	public bool Retweeted { get; set; }

	[JsonProperty("possibly_sensitive")]
	public bool PossiblySensitive { get; set; }

	[JsonProperty("filter_level")]
	public string FilterLevel { get; set; }

	[JsonProperty("lang")]
	public string Lang { get; set; }
}

public partial class User
{
	[JsonProperty("id")]
	public double Id { get; set; }

	[JsonProperty("id_str")]
	public string IdStr { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("screen_name")]
	public string ScreenName { get; set; }

	[JsonProperty("location")]
	public string Location { get; set; }

	[JsonProperty("url")]
	public Uri Url { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("translator_type")]
	public string TranslatorType { get; set; }

	[JsonProperty("protected")]
	public bool Protected { get; set; }

	[JsonProperty("verified")]
	public bool Verified { get; set; }

	[JsonProperty("followers_count")]
	public long FollowersCount { get; set; }

	[JsonProperty("friends_count")]
	public long FriendsCount { get; set; }

	[JsonProperty("listed_count")]
	public long ListedCount { get; set; }

	[JsonProperty("favourites_count")]
	public long FavouritesCount { get; set; }

	[JsonProperty("statuses_count")]
	public long StatusesCount { get; set; }

	[JsonProperty("created_at")]
	public string CreatedAt { get; set; }

	[JsonProperty("utc_offset")]
	public object UtcOffset { get; set; }

	[JsonProperty("time_zone")]
	public object TimeZone { get; set; }

	[JsonProperty("geo_enabled")]
	public bool GeoEnabled { get; set; }

	[JsonProperty("lang")]
	public string Lang { get; set; }

	[JsonProperty("contributors_enabled")]
	public bool ContributorsEnabled { get; set; }

	[JsonProperty("is_translator")]
	public bool IsTranslator { get; set; }

	[JsonProperty("profile_background_color")]
	public object ProfileBackgroundColor { get; set; }

	[JsonProperty("profile_background_image_url")]
	public string ProfileBackgroundImageUrl { get; set; }

	[JsonProperty("profile_background_image_url_https")]
	public string ProfileBackgroundImageUrlHttps { get; set; }

	[JsonProperty("profile_background_tile")]
	public bool ProfileBackgroundTile { get; set; }

	[JsonProperty("profile_link_color")]
	public string ProfileLinkColor { get; set; }

	[JsonProperty("profile_sidebar_border_color")]
	public object ProfileSidebarBorderColor { get; set; }

	[JsonProperty("profile_sidebar_fill_color")]
	public object ProfileSidebarFillColor { get; set; }

	[JsonProperty("profile_text_color")]
	public object ProfileTextColor { get; set; }

	[JsonProperty("profile_use_background_image")]
	public bool ProfileUseBackgroundImage { get; set; }

	[JsonProperty("profile_image_url")]
	public Uri ProfileImageUrl { get; set; }

	[JsonProperty("profile_image_url_https")]
	public Uri ProfileImageUrlHttps { get; set; }

	[JsonProperty("default_profile")]
	public bool DefaultProfile { get; set; }

	[JsonProperty("default_profile_image")]
	public bool DefaultProfileImage { get; set; }

	[JsonProperty("following")]
	public object Following { get; set; }

	[JsonProperty("follow_request_sent")]
	public object FollowRequestSent { get; set; }

	[JsonProperty("notifications")]
	public object Notifications { get; set; }

	[JsonProperty("profile_banner_url", NullValueHandling = NullValueHandling.Ignore)]
	public Uri ProfileBannerUrl { get; set; }
}
#endregion