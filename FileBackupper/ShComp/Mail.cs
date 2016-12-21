using System.Collections.Generic;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace ShComp
{
    static class Mail
    {
        public static async Task Send(string from, string name, IEnumerable<string> to, string host, int port, string password, bool enableSsl, string subject, string body)
        {
            using (var msg = new MailMessage())
            {
                msg.From = new MailAddress(from, name);
                foreach (var item in to)
                {
                    msg.To.Add(item);
                }
                msg.Subject = subject;
                msg.Body = body;
                msg.IsBodyHtml = false;

                using (var sc = new SmtpClient())
                {
                    sc.Host = host;
                    if (!string.IsNullOrEmpty(password))
                    {
                        sc.Credentials = new NetworkCredential(from, password);
                    }
                    sc.Port = port;
                    sc.EnableSsl = enableSsl;
                    await sc.SendMailAsync(msg);
                }
            }
        }
    }
}
