#region Namespaces
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
#endregion

namespace RevitWireEditor
{
    /// <summary>
    /// Isola a diferenca de API entre versoes do Revit para ler a lista de "Materiais"
    /// da configuracao de fiacao (o dropdown "Material:" em Configuracoes Eletricas >
    /// Tamanhos de fiacao).
    ///
    /// - Revit 2026+: a lista virou <see cref="ConductorMaterial"/>
    ///   (WireMaterialType foi deprecado). Enumeramos por
    ///   ConductorMaterial.GetConductorMaterialIds(doc).
    /// - Revit 2021-2025: a lista e <see cref="WireMaterialType"/>, acessivel por
    ///   ElectricalSetting.WireMaterialTypes.
    ///
    /// Em ambos os casos os itens sao Elementos do documento, entao o rename padrao
    /// (Element.Name = ...) vale para os dois caminhos.
    /// </summary>
    internal static class WireMaterialGateway
    {
        /// <summary>
        /// Se esta versao do Revit permite renomear materiais de fiacao via API.
        /// Pre-2026 (<see cref="WireMaterialType"/>): o Name e read-only e o setter lanca
        /// InvalidOperationException (confirmado em runtime no Revit 2022). So o Revit 2026+
        /// (<see cref="ConductorMaterial"/>) permite renomear.
        /// </summary>
#if CABLESAPI
        public const bool RenomearSuportado = true;
#else
        public const bool RenomearSuportado = false;
#endif

        /// <summary>
        /// Todos os itens cujos nomes o editor gerencia: materiais de fiacao/condutores
        /// (aba "Detalhes do condutor" / dropdown "Material") + tipos de cabo (aba
        /// "Tipos de cabo"). Tudo em uma lista de Elementos para renomear de forma uniforme.
        /// </summary>
        public static List<Element> GetItensRenomeaveis(Document doc)
        {
            var itens = new List<Element>();
            itens.AddRange(GetMateriais(doc));
            itens.AddRange(GetTiposDeCabo(doc));
            return itens;
        }

        /// <summary>
        /// Tipos de cabo (aba "Tipos de cabo"). Conceito introduzido no Revit 2026
        /// (<see cref="CableType"/>); nas versoes anteriores retorna lista vazia.
        /// </summary>
        public static List<Element> GetTiposDeCabo(Document doc)
        {
#if CABLESAPI
            return CableType.GetCableTypeIds(doc)
                .Select(id => doc.GetElement(id))
                .Where(e => e != null)
                .ToList();
#else
            return new List<Element>();
#endif
        }

        public static List<Element> GetMateriais(Document doc)
        {
#if CABLESAPI
            return ConductorMaterial.GetConductorMaterialIds(doc)
                .Select(id => doc.GetElement(id))
                .Where(e => e != null)
                .ToList();
#else
            var settings = ElectricalSetting.GetElectricalSettings(doc);
            var materiais = new List<Element>();
            foreach (WireMaterialType wmt in settings.WireMaterialTypes)
            {
                materiais.Add(wmt);
            }
            return materiais;
#endif
        }
    }
}
