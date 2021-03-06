﻿using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using EmailService.Configurations;
using EmailService.Models;
using EmailService.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EmailService.Service.Implementations
{
	public class SendGridEmailServiceDefinition : IEmailServiceDefinition
	{
		public string GetBody(EmailConfiguration emailConfiguration, Email email)
		{
			string contentType = ContentTypeUtility.GetContentTypeString(email.ContentType);
			string[] receivers = email.To.Select(t => t.ToString()).ToArray();
			
			return new SendGridBodyGenerator().Generate(receivers, email.Subject, emailConfiguration.FromName,
				emailConfiguration.FromAddress, contentType, email.Content);
		}

		public string GetAuthenticationHeaderScheme()
		{
			return "Bearer";
		}

		public string GetAuthenticationHeaderValue(ServiceConfiguration serviceConfiguration)
		{
			return serviceConfiguration.EmailServiceApiKey;
		}

		public string GetIdFromResponse(HttpResponseMessage response)
		{
			IEnumerable<string> headerValues;
			bool hasHeader = response.Headers.TryGetValues("X-Message-Id", out headerValues);

			if (hasHeader)
			{
				return headerValues.First();
			}
			else
			{
				return null;
			}
		}
	}

	class SendGridBodyGenerator
	{
		/*
		 * Format:
		 *
		 * {
			 "personalizations": [
			   {
				 "to": [
				   {
					 "email": "john@example.com"
				   }
				 ],
				 "subject": "Hello, World!"
			   }
			 ],
			 "from": {
			   "email": "from_address@example.com"
			 },
			 "content": [
			   {
				 "type": "text/plain",
				 "value": "Hello, World!"
			   }
			 ]
		   }
		 */
		
		public string Generate(string[] receiverEmails, string subject, string fromName, string fromEmail,
			string emailContentType, string emailContent)
		{
			SendGridReceiver[] receiverArray = receiverEmails.Select(r => new SendGridReceiver() { Email = r}).ToArray();
			
			SendGridPersonalization personalization = new SendGridPersonalization() { Subject = subject, To = receiverArray};
			SendGridContent content = new SendGridContent() { Type = emailContentType, Value = emailContent };
			SendGridSender sender = new SendGridSender() { Name = fromName, Email = fromEmail };
			
			SendGridPersonalization[] personalizations = { personalization };
			SendGridContent[] contentArray = { content };
			
			SendGridBody body = new SendGridBody() { Personalizations = personalizations, From = sender, Content = contentArray};

			return JObject.FromObject(body).ToString(Formatting.None);
		}
	}

	class SendGridBody
	{
		public SendGridPersonalization[] Personalizations { get; set; }
		public SendGridSender From { get; set; }
		public SendGridContent[] Content { get; set; }
	}

	class SendGridPersonalization
	{
		public string Subject { get; set; }
		public SendGridReceiver[] To { get; set; }
	}

	class SendGridReceiver
	{
		public string Email { get; set; }
	}

	class SendGridSender
	{
		public string Name { get; set; }
		public string Email { get; set; }
	}

	class SendGridContent
	{
		public string Type { get; set; }
		public string Value { get; set; }
	}
}