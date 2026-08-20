using Autodesk.Revit.DB;

namespace Kalidis.Revit;

/// <summary>
/// Pré-validação comum para alterações no modelo.
/// O objetivo é falhar antes da transação sempre que o estado do Revit já indicar
/// que a operação é insegura ou impossível.
/// </summary>
public static class RevitExecutionSafety
{
    public static SafetyResult CheckDocument(Document? doc, bool mutation)
    {
        if (doc == null)
            return SafetyResult.Fail("Nenhum documento Revit ativo.");
        if (!doc.IsValidObject)
            return SafetyResult.Fail("O documento Revit não é mais válido.");
        if (mutation && doc.IsReadOnly)
            return SafetyResult.Fail("O documento está somente leitura.");
        return SafetyResult.Ok();
    }

    public static SafetyResult CheckElements(
        Document doc,
        IEnumerable<ElementId> ids,
        bool allowGrouped = false,
        bool allowOwnedByOtherUser = false)
    {
        List<string> problems = new();
        foreach (ElementId id in ids.Distinct())
        {
            Element? element = doc.GetElement(id);
            if (element == null || !element.IsValidObject)
            {
                problems.Add($"Elemento {id.Value}: inexistente ou inválido.");
                continue;
            }

            if (!allowGrouped && element.GroupId != ElementId.InvalidElementId)
                problems.Add($"Elemento {id.Value}: pertence ao grupo {element.GroupId.Value}.");

            if (!allowOwnedByOtherUser && IsOwnedByOtherUser(doc, id))
                problems.Add($"Elemento {id.Value}: pertence/está bloqueado por outro usuário no worksharing.");
        }

        return problems.Count == 0
            ? SafetyResult.Ok()
            : SafetyResult.Fail(string.Join(" | ", problems));
    }

    /// <summary>
    /// Deve ser chamado dentro de uma Transaction já aberta.
    /// Retorna true em repin quando o elemento estava fixado e foi temporariamente desafixado.
    /// </summary>
    public static bool TryPrepareForTransform(
        Element element,
        bool autoUnpin,
        out bool repin,
        out string? error)
    {
        repin = false;
        error = null;
        try
        {
            if (!element.Pinned) return true;
            if (!autoUnpin)
            {
                error = $"Elemento {element.Id.Value} está fixado (Pinned).";
                return false;
            }

            element.Pinned = false;
            repin = true;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Elemento {element.Id.Value}: não foi possível desafixar. {ex.Message}";
            return false;
        }
    }

    public static void RestorePinned(Element? element, bool repin)
    {
        if (!repin || element == null || !element.IsValidObject) return;
        try { element.Pinned = true; } catch { }
    }

    public static bool IsOwnedByOtherUser(Document doc, ElementId id)
    {
        if (!doc.IsWorkshared) return false;
        try
        {
            CheckoutStatus status = WorksharingUtils.GetCheckoutStatus(doc, id);
            return status == CheckoutStatus.OwnedByOtherUser;
        }
        catch
        {
            // Se o Revit não consegue consultar o estado, não inventamos bloqueio.
            return false;
        }
    }

    public static object DescribeElement(Document doc, ElementId id)
    {
        Element? e = doc.GetElement(id);
        if (e == null)
            return new { elementId = id.Value, exists = false };

        long? hostId = null;
        if (e is FamilyInstance fi && fi.Host != null)
            hostId = fi.Host.Id.Value;

        return new
        {
            elementId = e.Id.Value,
            exists = true,
            name = e.Name,
            category = e.Category?.Name,
            typeId = e.GetTypeId().Value,
            pinned = e.Pinned,
            groupId = e.GroupId == ElementId.InvalidElementId ? (long?)null : e.GroupId.Value,
            hostId,
            ownedByOtherUser = IsOwnedByOtherUser(doc, e.Id)
        };
    }

    public sealed record SafetyResult(bool Success, string? Error)
    {
        public static SafetyResult Ok() => new(true, null);
        public static SafetyResult Fail(string error) => new(false, error);
    }
}
