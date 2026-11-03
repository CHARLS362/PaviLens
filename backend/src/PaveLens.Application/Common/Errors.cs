namespace PaveLens.Application.Common;

public sealed class NotFoundException(string resource, object key)
    : Exception($"{resource} '{key}' no existe.")
{
    public string Resource { get; } = resource;
}

/// <summary>The record changed since it was read (optimistic concurrency).</summary>
public sealed class ConcurrencyConflictException(Exception inner)
    : Exception("El recurso fue modificado por otra operación. Vuelve a cargarlo e inténtalo de nuevo.", inner);

/// <summary>An AI provider answered, but the answer breaks the expected contract.</summary>
public sealed class ModelOutputException(string message) : Exception(message);

/// <summary>An AI provider could not be reached or returned an error status.</summary>
public sealed class ModelUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
