using System.Linq;
using NUnit.Framework;
namespace Bubbles.XEvent.Tests
{
    [TestFixture]
    public class SsmsConnectionProviderTests
    {
        [Test]
        public void SsmsConnectionProvider_GetConnections_returns_contents_of_RegSrvrTest_xml()
        {
            var provider = new MCPServer.Services.SsmsConnectionProvider("RegSrvrTest.xml");
            var connections = provider.GetConnections();
            Assert.That(connections.Select(c => c.Name), 
                Is.EquivalentTo(["DatabaseEngineServerGroup/Group1/Name2", "DatabaseEngineServerGroup/Name1", "MruSqlConnectionsGroup/D:\\ssms\\Common7\\IDE/Connection0002", "MruSqlConnectionsGroup/D:\\ssms\\Common7\\IDE/Connection0001"]));
            var connection = provider.GetConnection("DatabaseEngineServerGroup/Group1/Name2");
            Assert.That(connection, Is.Not.Null, "Name2 should be found");
            Assert.That(connection.ServerName, Is.EqualTo("Name2"));
            if (System.OperatingSystem.IsWindows())
            {
                Assert.That(connection.AuthenticationType, Is.EqualTo("ActiveDirectoryDefault"), "On Windows, Active Directory Interactive should be converted to Active Directory Default by GetConnection");
            }
            else
            {
                Assert.That(connection.AuthenticationType, Is.EqualTo("ActiveDirectoryInteractive"));
            }
            connection = provider.GetConnection("DatabaseEngineServerGroup/Name1");
            Assert.That(connection, Is.Not.Null, "Name1 should be found");
            Assert.That(connection.ServerName, Is.EqualTo("test1"));
            Assert.That(connection.AuthenticationType, Is.EqualTo("Windows Authentication"));
            Assert.That(provider.DefaultConnectionName, Is.EqualTo("MruSqlConnectionsGroup/D:\\ssms\\Common7\\IDE/Connection0002"), "DefaultConnectionName should be last MRU value");
        }

        [Test]
        public void SsmsConnectionProvider_GetConnections_returns_empty_list_when_file_not_found()
        {
            var provider = new MCPServer.Services.SsmsConnectionProvider("NonExistentFile.xml");
            var connections = provider.GetConnections();
            Assert.That(connections, Is.Empty);
            Assert.That(provider.DefaultConnectionName, Is.EqualTo(string.Empty), "DefaultConnectionName should be empty when no connections found");
        }
    }
}
