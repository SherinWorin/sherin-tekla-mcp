namespace TeklaAiAssistant.Commands;

using TeklaAiAssistant.Core.Commands;

/// <summary>Reads value-only information from the current selection.</summary>
public sealed class ReadSelectionCommand : ITeklaCommand
{
    public string Id => "model.read-selection";

    public CommandResult Execute(ICommandContext context)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        try
        {
            if (!context.Session.IsConnected)
                return CommandResult.Failed("Tekla is not connected to an open model.");

            var parts = context.Session.GetSelectedParts();
            if (parts.Count == 0)
            {
                const string clearMessage = "No parts are selected; the selection result is clear.";
                context.Log.Info(clearMessage);
                return new CommandResult(true, clearMessage, Array.Empty<SelectedPartInfo>());
            }

            var unavailableLengths = parts.Count(part => !part.LengthMm.HasValue);
            var message = $"Read {parts.Count} selected part(s). " +
                          $"LENGTH report unavailable for {unavailableLengths} part(s). " +
                          "Available LENGTH report values assume millimetres; units are not independently verified.";
            context.Log.Info(message);
            return new CommandResult(true, message, parts);
        }
        catch (Exception exception)
        {
            context.Log.Error("Reading the Tekla selection failed.", exception);
            return CommandResult.Failed("Reading the Tekla selection failed: " + exception.Message);
        }
    }
}
