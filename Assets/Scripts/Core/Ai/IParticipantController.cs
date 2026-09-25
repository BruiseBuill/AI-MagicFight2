using System;

namespace MagicBrawl.Core
{
    /// <summary>异步决策端口：本地输入、AI、未来网络控制器均通过相同接口提交。</summary>
    public interface IParticipantController
    {
        bool UsesLocalInput { get; }
        void BeginDecision(DecisionRequest request);
        bool Submit(DecisionResponse response);
        bool TryTakeResponse(out DecisionResponse response);
        void Cancel();
    }

    public sealed class HumanController : IParticipantController
    {
        private DecisionRequest _request;
        private DecisionResponse _response;
        public bool UsesLocalInput { get { return true; } }
        public void BeginDecision(DecisionRequest request) { _request = request; _response = null; }
        public bool Submit(DecisionResponse response)
        {
            if (_request == null || response == null || response.Seat != _request.Seat || _response != null) return false;
            _response = response; return true;
        }
        public bool TryTakeResponse(out DecisionResponse response)
        {
            response = _response;
            if (response == null) return false;
            Cancel(); return true;
        }
        public void Cancel() { _request = null; _response = null; }
    }

    public sealed class AiController : IParticipantController
    {
        private readonly IAgent _agent;
        private DecisionResponse _response;
        public bool UsesLocalInput { get { return false; } }
        public bool Submit(DecisionResponse response) { return false; }
        public AiController(IAgent agent) { _agent = agent ?? throw new ArgumentNullException(nameof(agent)); }
        public void BeginDecision(DecisionRequest request) { _response = _agent.Decide(request); }
        public bool TryTakeResponse(out DecisionResponse response)
        {
            response = _response; _response = null; return response != null;
        }
        public void Cancel() { _response = null; }
    }
}
