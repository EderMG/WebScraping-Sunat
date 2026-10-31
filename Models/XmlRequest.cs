namespace SunatXmlApi;

public sealed record XmlRequest(
    string RucComprobante,
    string Tipo,
    string Serie,
    string Numero,
    string TipoServicio);
