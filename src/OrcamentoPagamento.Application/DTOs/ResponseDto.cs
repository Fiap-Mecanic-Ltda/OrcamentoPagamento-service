namespace OrcamentoPagamento.Application.DTOs
{
    /// <summary>
    /// Envelope de resposta idêntico ao do monólito, de propósito: o host Web já sabe ler
    /// esse formato, então trocar a chamada em processo por HTTP não muda as views.
    /// </summary>
    public class ResponseDto<T>
    {
        private List<string> _lstErros { get; set; } = new List<string>();
        private T _objResponse { get; set; } = default!;

        public bool hasErrors { get { return _lstErros != null && _lstErros.Count > 0 ? true : false; } }

        public ResponseDto<T> addError(string msg)
        {
            if (!string.IsNullOrWhiteSpace(msg))
                if (!_lstErros.Any(x => x.ToLower().Trim() == msg.ToLower().Trim()))
                    _lstErros.Add(msg);

            return this;
        }

        public ResponseDto<T> addError(Exception ex)
        {
            if (ex != null && !string.IsNullOrWhiteSpace(ex.Message))
            {
                var msg = ex.Message.ToLower().IndexOf("[show]") != -1
                    ? ex.Message.Replace("[show]", "")
                    : ex.Message;

                addError(msg);
            }
            return this;
        }

        public List<string> getErrors { get { return _lstErros; } }

        public ResponseDto<T> setResponse(T obj)
        {
            _objResponse = obj;
            return this;
        }

        public T getResponse { get { return _objResponse; } }
    }
}
