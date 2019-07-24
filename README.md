# Twitter Video Generator

Enter a Hashtag, get a video timeline.

This project started as a way of capturing the progression of the Region of Waterloo's ION Light Rail system, that opened June 21<sup>st</sup>, 2019.  It takes a search query, downloads the associated tweets, downloads the associated videos to the tweets, generates `srt` format subtitles with the Tweet information and generates the required FFMPEG command to concatenate the videos together.

_Shameless plug_: Here is the final copy of the generated video this project was written for, [_ION Light Rail as seen from #wrLRT_](https://youtu.be/__nGhZ2P25E).

# Tech Used

* [LINQPad](https://www.linqpad.net/)
* [Twitter Full Archive Search API](https://developer.twitter.com/en/docs/tweets/search/guides/fas-timeline.html)
* [MediaInfo NuGet Wrapper](https://github.com/StefH/MediaInfo.DotNetWrapper)
* [FFMPEG](https://ffmpeg.org/)
    - [libx264](https://www.videolan.org/developers/x264.html)
    - [Advanced Audio Coding (AAC)](https://trac.ffmpeg.org/wiki/Encode/AAC)
    - [SRT Subtitle Format](https://en.wikipedia.org/wiki/SubRip)
    - [ASS Subtitle Format](https://en.wikipedia.org/wiki/SubStation_Alpha)

# Running

1. Open in LINQPad
1. Set your `videoDirectory`, make sure the directory exists.
1. Change the values of the Twitter API bits, `consumerKey`, `consumerSecret`, `tokenValue`, `tokenSecret`.
1. Add your own `query`.  
1. Run it and 🤞🏻, 😉, it is still rough around the edges.

You will get an output to your video directory of two files.

* `playlist.txt` - This file contains the generated FFMPEG command line command that can copied and be run in Powershell.
* `playlist - complexFilters.txt` - This file contains the generated complex filter chain that is used by FFMPEG to [concatenate](https://trac.ffmpeg.org/wiki/Concatenate) (`concat`) the videos together.

# Complex Filters

FFMPEG can perform complex stream manipulations through the use of [filters](https://ffmpeg.org/ffmpeg-filters.html).  In order to perform operations, like burning subtitles onto videos of different shapes and sizes or concatenate various videos together, a complex filter graph is needed.

## Different Sizes

Not all videos downloaded from Twitter are the same shape and size.  Even though the script tries to do its best to download everything in 720p (1280 x 720), some of the videos are portrait (720 x 1280), and others have other aspect ratios.  This is a challenge when trying to concatenate them all into one video.  The script uses the [MediaInfo NuGet Wrapper](https://github.com/StefH/MediaInfo.DotNetWrapper) to determine various parameters about the videos and different filters are applied depending on what is found.

### Portrait Videos

Vertical videos are really annoying to handle, but none the less people insist on them.  When a portrait video is found they are tracked and when the filter graph is generated they are converted to landscape 720p with the following filter. ([Stack Overflow solution](https://stackoverflow.com/a/30832903))

```cs
"scale=ih*16/9:-1,boxblur=luma_radius=min(h\,w)/20:luma_power=1:chroma_radius=min(cw\,ch)/20:chroma_power=1[bg];[bg]{videoIndex}overlay=(W-w)/2:(H-h)/2,crop=h=iw*9/16[vid]; [vid]scale=1280:720[asd]; [asd]"
```

In this filter the [] items represent named variables of data used in the filter chain.  They can be used to pick a part parts of the input, as well as define intermediate components and output streams

Here is a sample, from the Stack Overflow solution, that illustrates the effect, [https://www.youtube.com/watch?v=CgZsDLfzrTs](https://www.youtube.com/watch?v=CgZsDLfzrTs).

### Odd Shaped Videos

Some videos came in different aspect ratios from the regular horizontal/portrait 16:9 720p videos.  An example of this are 3:2 videos from high quality cameras, or 1:1 videos from Instagram users 😅.  These videos are handled through a special filter in the filter graph, but it only adds black bars to correct the aspect ratio and have it match 16:9 720p. ([Super User solution](https://superuser.com/a/991412))

```cs
"scale=w=1280:h=720:force_original_aspect_ratio=1,pad=1280:720:(ow-iw)/2:(oh-ih)/2[asd]; [asd]"
```

### Subtitle

The Tweet information is burned onto the video using the [`subtitles`](https://trac.ffmpeg.org/wiki/HowToBurnSubtitlesIntoVideo) filter.  This filter hands the fonts and everything for you.  The subtitles used for the tweets is the `srt` format that is quite prevalent.  Each video gets an 8 second subtitle with one entry containing the Tweet, the author, and month & year the Tweet was made.  Here is a sample of one of the `srt` files.

```
1
00:00:00,000 --> 00:00:08,000
Rain, rain go away, come again another day! ION 507 zips up King St. heading northbound. #wrLRT #myIONtheTrain #ReadytoRideION https://t.co/EWDEesabcV
<i>@IanBMorris – May 2019</i>
```

#### Title Cards

In a last minute add, in order to title the video, a quick way of making title cards was devised.  Using the [Advanced SubStation Alpha (ASS)](https://en.wikipedia.org/wiki/SubStation_Alpha#Advanced_SubStation_Alpha) subtitle format, which has rich support for fonts, colours, positioning, and more, simple title cards were added to the filter graph.  In order to to this a dummy video source was used that is generated on-the-fly by FFMPEG and is just a solid background colour.

##### Dummy Video Input 

At the end of the FFMPEG input list, eg. `ffmpeg -i video.mp4 -i video2.mp4 ...` the following was added to generate a solid colour background to overlay the title cards on.

```
-f lavfi -i color=c="#006bb6":s=1280x720:d=40
```

##### Title Card Subtitle Filter

The `ass` filter was used to burn the title card subtitles onto the video.
```
[296:v]ass=I\\:\\\\_\\\\Twitter videos\\\\test 11\\\\Title Cards.ass[v0]
```

### Filter for One Video

With all the pieces in place a filter for one video looks like the following.

#### Regular Video

```
[9:v:0]subtitles=I\\:\\\\_\\\\Twitter videos\\\\test 11\\\\ZSFdBV_OHgSg9lNf.srt[v10];
```

Here you can see input video with index 9's first video track `[9:v:0]` is the source for the subtitles filter and `[v10]` is the assigned output variable.

*Note*: the filter graph for the subtiles needs all these `\`s.

#### Portrait Video

```
[28:v:0]scale=ih*16/9:-1,boxblur=luma_radius=min(h\,w)/20:luma_power=1:chroma_radius=min(cw\,ch)/20:chroma_power=1[bg];[bg][28:v:0]overlay=(W-w)/2:(H-h)/2,crop=h=iw*9/16[vid]; [vid]scale=1280:720[asd]; [asd]subtitles=I\\:\\\\_\\\\Twitter videos\\\\test 11\\\\eJ9JlE4wygkowl85.srt[v29]
```

*Note*: The `[asd]` variable is a badly named temp variable to allow the scaling filter be the input to the subtitle filter.

### Concatenation

In order to concatenate the videos together with FFMPEG the [`concat`](https://trac.ffmpeg.org/wiki/Concatenate) filter is used.  See _Concatenation of files with different codecs_ at the linked page.  Once all the videos have their filter chains generated for them, and are assigned an output variable, they can be concatenated together

Simple concatenation looks like this.

```
[v0][4:a][v1][0:a:0][v2][4:a][v3][2:a:0]concat=n=4:v=1:a=1[outv][outa]
```

The `concat` filter takes in video and audio streams from multiple inputs (eg. `[v0][4:a][v1][0:a:0]`) and outputs a single video and a single audio stream, in this case named `[outv][outa]`.  The order of the streams has to be preserved throughout the input items and output.

Here, `[v0]` is a video generated with its own filter chain and `[4:a]` is dummy audio, see [Dummy Audio Input](#dummy-audio-input).  Since there are just audio and video streams here, every two [] are one video.  For videos that have their own audio track, like `[v1][0:a:0]`, they first audio channel is selected (like the video shown in [Regular Video](#regular-video)) and associated with its transformed video source.

Finally, `concat` is told how many video's it is concatenating, and what the stream order is, and the filter output is assigned.

#### Dummy Audio Input

Not all videos will have audio, so a dummy audio stream is used in place of the video clip's non-existent audio track. Similar to [Dummy Video Input](#dummy-video-input) above, the dummy audio input is added to the input section of the FFMPEG command.

```
-f lavfi -t 0.1 -i anullsrc
```

### Calling the Filter Graph

In order to tell FFMPEG to execute the filer graph the argument `-filter_complex_script` is added to the command with the path to the complex filter script.  The outputs are of the filter graph, `[outv]` and `[outa]` are then mapped to tell FFMPEG what stream to use, `-map "[outv]" -map "[outa]"`.

# FFMPEG Command

Below is a sample of command that the script generates. It incorporates the various input videos that were acquired from Twitter, along with some generated inputs to help with title card subtitle overlays and audioless clips, and applies a complex filter graph which corrects for the video anomalies and burns the generated subtitles onto the videos.  It then maps the output of the filter graph as the video and audio stream to be compressed.

This sample shows the use of [libx264](https://www.videolan.org/developers/x264.html), FFMPEG's [documentation](https://trac.ffmpeg.org/wiki/Encode/H.264), as the video codec, with an output framerate of 59.94 fps (60000/1001).  The audio here is being encoded using FFMPEG's [Advanced Audio Coding (AAC)](https://trac.ffmpeg.org/wiki/Encode/AAC) encoded at 128 kbps, 2 channel.

The reason the framerate, `-r`, and the number of audio channels, `-ac`, are being specified is due to not all the videos having the same formats.  Without them FFPEG will default to the first values it finds in the video and audio streams.  If they are odd then the output will be penalized.  This way it knows what the desired output should be.

```bash
ffmpeg -i video1.mp4 -i video2.mp4 -i video3.mp4 -f lavfi -i color=c="#006bb6":s=1280x720:d=40 -f lavfi -t 0.1 -i anullsrc -filter_complex_script 'I:\_\path\to\complex\filter.txt' -map "[outv]" -map "[outa]" -c:v libx264 -b:v 4.5M -r 60000/1001 -c:a aac -b:a 128k -ac 2 "I:\_\path\to\output\concat.mp4"
```

---

Please feel free to open [issues](https://github.com/ScottMorris/twitter-video-generator/issues) and reach out to me, I'd be happy to answer any FFMPEG questions to the best of my ability.