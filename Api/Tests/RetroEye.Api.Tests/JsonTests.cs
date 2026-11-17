using System.IO;
using System.Text;
using System.Threading;
using Xunit;

namespace RetroEye.Api.Tests
{
    /// <summary>The library's own JSON reader and writer, and the protocol's line reader.</summary>
    public class JsonTests
    {
        /// <summary>The library's JSON reads back what it writes, escapes and all, and refuses what isn't JSON.</summary>
        [Fact]
        public void JsonRoundTripsAndRefusesBadText()
        {
            var value = ApiJson.NewObject()
                .Set("text", "quote \" backslash \\ line\nbreak \u0001 é")
                .Set("number", 1.25).Set("whole", 42).Set("flag", true).Set("nothing", (string)null)
                .Set("list", ApiJson.NewArray().Add(ApiJson.From(1)).Add(ApiJson.From("two")));
            ApiJson back = ApiJson.Parse(value.ToString());
            Assert.Equal(value.ToString(), back.ToString());
            Assert.Equal("quote \" backslash \\ line\nbreak \u0001 é", back["text"].AsString());
            Assert.Equal(1.25, back["number"].AsNumber());
            Assert.Equal(42, back["whole"].AsInt());
            Assert.True(back["nothing"].IsNull);
            Assert.True(back["missing"]["deeper"].IsNull);
            Assert.Equal("two", back["list"][1].AsString());
            Assert.Equal(value.ToString(), ApiJson.Parse(value.ToIndentedString()).ToString());

            foreach (string bad in new[] { "", "{", "{\"a\":}", "[1,]", "{\"a\":1} x", "'single'", "{a:1}", "\"\u0001\"", new string('[', 100) + new string(']', 100) })
            {
                Assert.False(ApiJson.TryParse(bad, out _), bad);
            }
        }

        /// <summary>Every recorded state reads and writes back exactly as RetroEye wrote it.</summary>
        [Fact]
        public void RecordedStatesRoundTrip()
        {
            Assert.NotEmpty(TestFiles.StateNames());
            foreach (string name in TestFiles.StateNames())
            {
                string text = TestFiles.StateText(name).Replace("\r\n", "\n").TrimEnd('\n');
                Assert.Equal(text, ApiJson.Parse(text).ToIndentedString().Replace("\r\n", "\n").TrimEnd('\n'));
            }
        }

        /// <summary>Lines are read one at a time, a "\r" before the newline dropped; a line over the limit is skipped and reported.</summary>
        [Fact]
        public void LinesAreReadWithinTheLimit()
        {
            string text = "{\"a\":1}\r\n" + new string('x', 100) + "\n{\"b\":\"é\"}\nlast";
            var reader = new ApiLineReader(new MemoryStream(Encoding.UTF8.GetBytes(text)), 50);
            Assert.Equal("{\"a\":1}", reader.ReadLineAsync(CancellationToken.None).Result);
            Assert.False(reader.LastWasTooLong);
            Assert.Equal("", reader.ReadLineAsync(CancellationToken.None).Result);
            Assert.True(reader.LastWasTooLong);
            Assert.Equal("{\"b\":\"é\"}", reader.ReadLineAsync(CancellationToken.None).Result);
            Assert.Equal("last", reader.ReadLineAsync(CancellationToken.None).Result);
            Assert.Null(reader.ReadLineAsync(CancellationToken.None).Result);
        }
    }
}
