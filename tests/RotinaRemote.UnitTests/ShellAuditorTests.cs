using System;
using System.IO;
using System.Threading.Tasks;
using RotinaRemote.Core.Logging;
using Xunit;

namespace RotinaRemote.UnitTests
{
    public class ShellAuditorTests
    {
        [Fact]
        public async Task ShellAuditor_AuditConnectionAtConnectAsync_WritesToLogShellFile()
        {
            // Arrange
            ShellAuditor.InitializeLogPaths();
            string testTargetId = "999 888 777";
            string testTransport = "Servidor Relay WebSocket (Cloud)";

            // Act
            await ShellAuditor.AuditConnectionAtConnectAsync("Teste Saída (Cliente)", testTargetId, testTransport);

            // Assert
            string localLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log-shell.txt");
            Assert.True(File.Exists(localLog), $"Ficheiro log-shell.txt deve existir em {localLog}");

            string content = File.ReadAllText(localLog);
            Assert.Contains("AUDITORIA DA SHELL LOCAL", content);
            Assert.Contains("AUDITORIA DA SHELL DO GOOGLE CLOUD RUN", content);
            Assert.Contains("ANÁLISE DE CONECTIVIDADE À INTERNET", content);
            Assert.Contains(testTargetId, content);
        }

        [Fact]
        public void ShellAuditor_LogSessionEnded_WritesClosureReport()
        {
            // Arrange
            ShellAuditor.InitializeLogPaths();
            string testTargetId = "111 222 333";

            // Act
            ShellAuditor.LogSessionEnded(testTargetId, TimeSpan.FromMinutes(5));

            // Assert
            string localLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log-shell.txt");
            Assert.True(File.Exists(localLog));

            string content = File.ReadAllText(localLog);
            Assert.Contains("[SESSÃO TERMINADA]", content);
            Assert.Contains(testTargetId, content);
            Assert.Contains("0 comandos foram executados em shells", content);
        }
    }
}
