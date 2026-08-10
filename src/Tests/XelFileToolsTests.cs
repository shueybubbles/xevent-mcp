using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Bubbles.XEvent.MCPServer.Tools;

namespace Bubbles.XEvent.Tests
{
    [TestFixture]
    public class XelFileToolsTests
    {
        private static string TestFilePath => System.IO.Path.Combine(TestContext.CurrentContext.TestDirectory, "twoevents.xel");

        [Test]
        public async Task ListEventsInXelFile_read_full_file_returns_all_events()
        {
            // Arrange
            var filePath = TestFilePath;
            long maxEvents = 10;
            long byteOffset = 0;
            var cancellationToken = new System.Threading.CancellationToken();
            // Act
            var result = await XelFileTools.ListEventsInXelFile(filePath, cancellationToken, maxEvents, byteOffset);
            // Assert
            Assert.That(result, Is.Not.Null.And.StartsWith("The end of the file has been reached. Total events read: 2. Events: [{\"Name\":\"sql_batch_starting\""));
        }

        [Test]
        public async Task ListEventsInXelFile_read_single_event_returns_byteoffset_for_subsequent_reads()
        {
            // Arrange
            var filePath = TestFilePath;
            long maxEvents = 1;
            long byteOffset = 0;
            var cancellationToken = new System.Threading.CancellationToken();
            // Act
            var result = await XelFileTools.ListEventsInXelFile(filePath, cancellationToken, maxEvents, byteOffset);
            // Assert
            Assert.That(result, Is.Not.Null.And.StartsWith("Total events read: 1. More events may be available at byte offset 25109"));
            
            result = await XelFileTools.ListEventsInXelFile(filePath, cancellationToken, maxEvents, 25109);
            Assert.That(result, Is.Not.Null.And.StartsWith("Total events read: 1. More events may be available at byte offset 25325. Events: [{\"Name\":\"sql_batch_starting\",\"UUID\":\"64e8eccc-1de0-405c-9b49-a4ea488fe9a4\","));
        }
    }
}
