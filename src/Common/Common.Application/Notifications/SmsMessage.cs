using System;
using System.Collections.Generic;
using System.Text;

namespace Common.Application.Notifications;

public sealed record SmsMessage(
    string PhoneNumber,
    string Message);
