namespace OpsBridge.Domain.Exceptions;

public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
    public DomainException(string message, Exception innerException) : base(message, innerException) { }
}

public class SignatureValidationException : DomainException
{
    public SignatureValidationException(string message) : base(message) { }
}

public class TimestampExpiredException : DomainException
{
    public TimestampExpiredException(string message) : base(message) { }
}

public class InvalidEventContractException : DomainException
{
    public InvalidEventContractException(string message) : base(message) { }
}

public class DuplicateEventException : DomainException
{
    public Guid EventId { get; }

    public DuplicateEventException(Guid eventId, string message) : base(message)
    {
        EventId = eventId;
    }
}

public class EventPayloadConflictException : DomainException
{
    public Guid EventId { get; }

    public EventPayloadConflictException(Guid eventId, string message) : base(message)
    {
        EventId = eventId;
    }
}
