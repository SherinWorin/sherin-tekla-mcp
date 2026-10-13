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

            var selection = context.Session.ReadSelection();
            if (selection.ReportedPartCount == 0)
            {
                const string clearMessage = "No parts are selected; the selection result is clear.";
                context.Log.Info(clearMessage);
                return new CommandResult(true, clearMessage, selection);
            }

            var unavailableLengths = selection.Objects.Count(part => !part.LengthMm.HasValue);
            var message = $"Read {selection.ReportedPartCount} selected part(s). " +
                          $"LENGTH report unavailable for {unavailableLengths} part(s). " +
                          "LENGTH and WEIGHT units are unverified; LENGTH currently assumes millimetres.";
            context.Log.Info(message);
            return new CommandResult(true, message, selection);
        }
        catch (Exception exception)
        {
            context.Log.Error("Reading the Tekla selection failed.", exception);
            return CommandResult.Failed("Reading the Tekla selection failed: " + exception.Message);
        }
    }
}
