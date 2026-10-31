using DaisyOS.Continuity.Protocol.V1;

namespace DaisyOS.Continuity;

public sealed partial class ContinuityDaemon
{
    private InputPosition GetInputPosition(string id)
    {
        lock (_gate)
        {
            var peer = _state.Settings.Peers[id];
            // Establish one deterministic baseline for existing pairings, whose
            // independently saved defaults may put both computers on the same side.
            if (peer.EdgeRevision == 0 && string.CompareOrdinal(_state.Id, id) < 0)
            {
                peer = peer with { EdgeRevision = 1, EdgeAuthor = _state.Id };
                _state.Settings.Peers[id] = peer;
                SaveSettings();
            }
            return new() { Edge = peer.InputEdge, Revision = peer.EdgeRevision, Author = peer.EdgeAuthor };
        }
    }

    internal void ApplyInputPosition(string id, InputPosition? position)
    {
        if (position is null || position.Edge is not ("left" or "right" or "top" or "bottom") ||
            position.Revision <= 0 || position.Author.Length != 64) return;
        lock (_gate)
        {
            Require(id, null);
            if (position.Author != id && position.Author != _state.Id) return;
            var peer = _state.Settings.Peers[id];
            if (position.Revision < peer.EdgeRevision || position.Revision == peer.EdgeRevision &&
                string.CompareOrdinal(position.Author, peer.EdgeAuthor) <= 0) return;
            var edge = position.Edge switch { "left" => "right", "right" => "left", "top" => "bottom", _ => "top" };
            _state.Settings.Peers[id] = peer with { InputEdge = edge, EdgeRevision = position.Revision, EdgeAuthor = position.Author };
            SaveSettings();
            if (edge != peer.InputEdge)
                foreach (var live in _live.Values.Where(l => l.Info.PeerId == id && l.Info.Kind == "input" && l.Info.Outgoing))
                    _ = live.Stop.CancelAsync();
        }
    }
}
