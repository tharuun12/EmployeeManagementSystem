//using EMS;
//using EMS.Models;
//using Microsoft.Extensions.Options;
//using System;
//using System.Net.Mail;
//using System.Threading.Tasks;
//using Xunit;

//namespace EMS.Tests.Services
//{
//    public class EmailServiceTests
//    {
//        private EmailSettings GetValidSettings() => new EmailSettings
//        {
//            From = "from@example.com",
//            SmtpServer = "smtp.example.com",
//            Port = 587,
//            Username = "user",
//            Password = "pass"
//        };

//        [Fact]
//        public async Task SendEmailAsync_WithValidParameters_ThrowsIfSmtpNotAvailable()
//        {
//            // Arrange
//            var options = Options.Create(GetValidSettings());
//            var service = new EMS.Services.Implementations.EmailService(options);

//            // Act & Assert
//            // Since no SMTP server is available, expect SmtpException or InvalidOperationException
//            await Assert.ThrowsAnyAsync<Exception>(() =>
//                service.SendEmailAsync("to@example.com", "Test Subject", "Test Body"));
//        }

//        [Theory]
//        [InlineData(null, "Subject", "Body")]
//        [InlineData("to@example.com", null, "Body")]
//        [InlineData("to@example.com", "Subject", null)]
//        [InlineData("", "Subject", "Body")]
//        [InlineData("to@example.com", "", "Body")]
//        [InlineData("to@example.com", "Subject", "")]
//        public async Task SendEmailAsync_WithInvalidParameters_ThrowsArgumentException(string to, string subject, string body)
//        {
//            // Arrange
//            var options = Options.Create(GetValidSettings());
//            var service = new EMS.Services.Implementations.EmailService(options);

//            // Act & Assert
//            await Assert.ThrowsAnyAsync<ArgumentException>(() =>
//                service.SendEmailAsync(to, subject, body));
//        }

//        [Fact]
//        public void EmailService_CanBeConstructed_WithValidOptions()
//        {
//            // Arrange & Act
//            var options = Options.Create(GetValidSettings());
//            var service = new EMS.Services.Implementations.EmailService(options);

//            // Assert
//            Assert.NotNull(service);
//        }
//    }
//}
