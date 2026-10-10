namespace Common.Application.Options;

/// <summary>
///     Overrides the configuration section an <c>*Options</c> class is bound from by <c>AddCommonOptions</c>.
///     Without it the section is the bare class name. Use it when the owning feature already binds the options from a
///     nested section (e.g. <c>Notifications:Email</c>) so there is exactly one source of truth.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class OptionsSectionAttribute(string path) : Attribute
{
    public string Path { get; } = path;
}
