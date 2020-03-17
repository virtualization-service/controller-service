namespace Parser.Model
{
    public class ElementStructure
    {
        public ElementStructure(string xmlValue, string xpath, int instance = 0)
        {
            XmlValue = xmlValue;
            Xpath = xpath;
            Instance = instance;
        }

        public string XmlValue { get; set; }

        public int Instance { get; set; }

        public string Xpath { get; set; }
    }
}