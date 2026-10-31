namespace SunatXmlApi.Configuration;

public sealed class SunatOptions
{
    public const string SectionName = "Sunat";

    public string LoginUrl { get; init; } = string.Empty;
    public string MenuUrl { get; init; } =
        "https://e-menu.sunat.gob.pe/cl-ti-itmenu/MenuInternet.htm?action=execute&code=11.38.1.1.1&s=ww1";
    public string ConsultationUrlTemplate { get; init; } =
        "https://api-cpe.sunat.gob.pe/v1/contribuyente/consultacpe/comprobantes/{ruc}-{tipo}-{serie}-{numero}-{tipoServicio}";
}
