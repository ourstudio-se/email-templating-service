﻿using System;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;
using EmailService.Configurations;
using EmailService.Dtos.Requests;
using EmailService.Dtos.Requests.Factories;
using EmailService.Models;
using EmailService.Service;
using EmailService.Utils;
using EmailService.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace EmailService.Controllers
{
    [Route("api/email/send")]
    [ApiController]
    public class EmailSendController : ControllerBase
    {
        private readonly EmailConfiguration _emailConfiguration;
        private readonly IEmailService _emailService;
        private readonly IHtmlGeneratorService _htmlGeneratorService;
        private readonly IEmailLoggingService _emailLoggingService;
        
        public EmailSendController(EmailConfiguration emailConfiguration, IEmailService emailService,
            IHtmlGeneratorService htmlGeneratorService, IEmailLoggingService emailLoggingService)
        {
            _emailConfiguration = emailConfiguration;
            _emailService = emailService;
            _htmlGeneratorService = htmlGeneratorService;
            _emailLoggingService = emailLoggingService;
        }
        
        // POST api/email/send
        
        /// <summary>
        /// Send an email generated by the specified template. The separation of content and personalContent is
        /// for GDPR purposes, and it means that both content and personalContent will be included in the email sent,
        /// but the logs will store the content in raw text, and the personalContent obfuscated.
        /// </summary>
        /// <param name="request">The email payload.</param>
        /// <response code="200">The email was sent and logs were created.</response>
        /// <response code="400">The payload was incorrect.</response>
        [HttpPost]
        [Route("", Name = "SendEmail")]
        public async Task<IActionResult> Post([FromBody] EmailSendRequest request)
        {
            bool isValidEmailRequest = EmailSendRequestFactory.IsValidEmailRequest(request);

            if (!isValidEmailRequest)
            {
                return new BadRequestObjectResult("Invalid email payload.");
            }

            MailAddress[] toAddresses;

            try
            {
                toAddresses = request.To.Select(t => new MailAddress(t)).ToArray();
            }
            catch (Exception exception)
            {
                return new BadRequestObjectResult($"Invalid format of recipient email {request.To}.");
            }

            bool hasAtLeastOneEmailAddress = toAddresses.Length > 0;

            if (!hasAtLeastOneEmailAddress)
            {
                return new BadRequestObjectResult("No receivers specified. Need at least one.");
            }

            Template template = TemplateUtility.GetTemplateByName(_emailConfiguration, request.Template);

            bool isInvalidTemplate = template == null;

            if (isInvalidTemplate)
            {
                return new BadRequestObjectResult($"A template with the name {request.Template} does not exist.");
            }

            JObject fullContent = JsonUtility.GetMergedJson(request.Content, request.PersonalContent);

            EmailViewModel emailViewModel = new EmailViewModel() {TemplateName = template.Name, Content = fullContent};
            string rawHtml = await _htmlGeneratorService.GetRawHtmlAsync("Email/Index", emailViewModel);

            bool hasNoRawHtml = rawHtml == null;

            if (hasNoRawHtml)
            {
                return new BadRequestObjectResult("Internal error.");
            }
            
            Email email = new Email(toAddresses, template.Subject, ContentType.TEXT_HTML, rawHtml);

            string emailServiceId;

            try
            {
                emailServiceId = await _emailService.SendEmailAsync(email);
            }
            catch (Exception e)
            {
                return new BadRequestObjectResult("Failed to send email.");
            }

            try
            {
                string[] receiverEmails = email.To.Select(t => t.ToString()).ToArray();

                await _emailLoggingService.LogAsync(emailServiceId, receiverEmails, template.Name,
                    request.PersonalContent, request.Content);

                return new OkResult();
            }
            catch (Exception e)
            {
                return new BadRequestObjectResult("Email was sent successfully, but logging failed unexpectedly.");
            }
        }
    }
}
