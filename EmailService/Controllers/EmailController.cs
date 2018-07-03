﻿using System;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;
using EmailService.Dtos.Requests;
using EmailService.Dtos.Requests.Factories;
using EmailService.Dtos.Responses;
using EmailService.Dtos.Responses.Factories;
using EmailService.Models;
using EmailService.Properties;
using EmailService.Service;
using EmailService.Utils;
using EmailService.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace EmailService.Controllers
{
	[Route("api/email")]
	[ApiController]
	public class EmailController : Controller
	{
		private readonly IHtmlGeneratorService _htmlGeneratorService;
		private readonly EmailProperties _emailProperties;
		private readonly IEmailLoggingService _loggingService;

		public EmailController(IHtmlGeneratorService htmlGeneratorService, EmailProperties emailProperties,
			IEmailLoggingService emailLoggingService)
		{
			_htmlGeneratorService = htmlGeneratorService;
			_emailProperties = emailProperties;
			_loggingService = emailLoggingService;
		}
		
		// GET api/email
		[HttpGet]
		[Route("", Name = "HealthCheck")]
		public async Task<IActionResult> Get()
		{
			return new OkResult();
		}
		
		// GET api/email/{id}
		[HttpGet]
		[Route("{id}", Name = "GetEmail")]
		[ProducesResponseType(typeof(SentEmailResponse), 200)]
		public async Task<IActionResult> Get([FromRoute] string id,
			[FromQuery(Name = "receiversToTest")] string receiversToTest = null)
		{
			bool isValidId = Guid.TryParse(id, out Guid guid);

			if (!isValidId)
			{
				return new BadRequestObjectResult("The id specified had an invalid format.");
			}

			LogEntry logEntry = await _loggingService.GetAsync(guid);

			bool noLogEntryFound = logEntry == null;

			if (noLogEntryFound)
			{
				return new BadRequestObjectResult("No logs found for the id provided.");
			}
			
			JObject content = JObject.Parse(logEntry.Content);
			JObject personalContent = JObject.Parse(logEntry.PersonalContent);
			
			EmailSendRequest request = new EmailSendRequest()
			{
				Content = content,
				PersonalContent = personalContent,
				Template = logEntry.Template,
				To = new string[0]
			};

			OkObjectResult actionResult = await Post(request) as OkObjectResult;
			EmailPreviewResponse previewResponse = (EmailPreviewResponse) actionResult.Value;

			string[] receiversToTestArray = RestUtility.GetArrayQueryParam(receiversToTest);

			bool hasReceiversToTestArray = receiversToTestArray != null && receiversToTestArray.Length > 0;
			bool isReceiversMatch;

			if (hasReceiversToTestArray)
			{
				isReceiversMatch = TestReceiversMatch(receiversToTestArray, logEntry);
			}
			else
			{
				isReceiversMatch = false;
			}

			SentEmailResponse response = SentEmailResponseFactory.Create(logEntry, previewResponse, isReceiversMatch);
			return new OkObjectResult(response);
		}

		// POST api/email
        [HttpPost]
        [Route("", Name = "PreviewEmail")]
        [ProducesResponseType(typeof(EmailPreviewResponse), 200)]
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

	        Template template = TemplateUtility.GetTemplateByName(_emailProperties, request.Template);

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
	        EmailPreviewResponse response = EmailPreviewResponseFactory.Create(email);
	        
	        return new OkObjectResult(response);
        }

		private bool TestReceiversMatch(string[] receiversToTestArray, LogEntry logEntry)
		{
			string[] logEntryTo = ArrayUtility.GetArrayFromCommaSeparatedString(logEntry.To);
			bool isSameSize = logEntryTo.Length == receiversToTestArray.Length;

			if (!isSameSize)
			{
				return false;
			}

			foreach (string receiver in receiversToTestArray)
			{
				string hashedReceiver = HashUtility.GetStringHash(receiver);
				bool isReceiverInLogEntry = logEntryTo.Contains(hashedReceiver);

				if (!isReceiverInLogEntry)
				{
					return false;
				}
			}

			return true;
		}
	}
}