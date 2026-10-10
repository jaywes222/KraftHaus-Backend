using System;
using System.Collections.Generic;
using System.Text;

namespace Common.Application.Notifications;

public sealed record EmailMessage(
    string To,
    string Subject,
    string TextBody,
    string? HtmlBody = null);
