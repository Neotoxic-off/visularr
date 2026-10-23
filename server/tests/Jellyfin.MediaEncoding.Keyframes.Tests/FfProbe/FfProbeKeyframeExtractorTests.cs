using System.IO;
using System.Text.Json;
using Xunit;

namespace Jellyfin.MediaEncoding.Keyframes.FfProbe
{
    public class FfProbeKeyframeExtractorTests
    {
        [Theory]
        [InlineData("keyframes.txt", "keyframes_result.json")]
        [InlineData("keyframes_streamduration.txt", "keyframes_streamduration_result.json")]
        public void ParseStream_Valid_Success(string testDataFileName, string resultFileName)
        {
            string testDataPath = Path.Combine("FfProbe/Test Data", testDataFileName);
            string resultPath = Path.Combine("FfProbe/Test Data", resultFileName);
            using FileStream resultFileStream = File.OpenRead(resultPath);
            KeyframeData expectedResult = JsonSerializer.Deserialize<KeyframeData>(resultFileStream)!;

            using FileStream fileStream = File.OpenRead(testDataPath);
            using StreamReader streamReader = new StreamReader(fileStream);

            KeyframeData result = FfProbeKeyframeExtractor.ParseStream(streamReader);

            Assert.Equal(expectedResult.TotalDuration, result.TotalDuration);
            Assert.Equal(expectedResult.KeyframeTicks, result.KeyframeTicks);
        }
    }
}
