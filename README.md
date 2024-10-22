# Starship Telemetry Extractor
StarshipTelemetryExtractor is a console application designed to extract the telemetry data shown on screen during SpaceX's starship launches.
It does this by ingesting a 1080p video file of the launch and snipping it up into many small images using [FFmpeg](https://www.ffmpeg.org/).
After this it then uses the [TesseractEngine](https://tesseract-ocr.github.io/) to perform Optical Character Recognition on these images and extract the telemetry data to the best of its abilities. 
This can sometimes be difficult or impossible due to the slightly translucent nature of the SpaceX telemetry bar, therefore i've also implemented some automatic systems to fill in any gaps in the telemetry and remove datapoints that lie far outside the normal.

### Why did I release the source?
I decided to make this project publicly available incase there's any people in this community who are interested in looking at at, and to those people: **please be kind!**
I'm just a junior software developer, making something as a small hobby project. I'm aware that there's some *not so great* coding practices in use, as I don't intend to make this tool available for anyone to download.
Feel free to contact me with constructive feedback, general questions or if you wish to help out!
