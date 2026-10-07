using System;
using System.Collections.Generic;
using System.Text;

namespace Identity.Domain.Sessions;

public enum AuthSessionRotationOutcome
{
    Rotated = 1,
    PreviousGenerationGrace = 2
}
