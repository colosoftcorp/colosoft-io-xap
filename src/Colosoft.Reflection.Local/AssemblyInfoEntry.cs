using System;

namespace Colosoft.Reflection.Local
{
    [Serializable]
    [System.Xml.Serialization.XmlSchemaProvider("GetMySchema")]
    public sealed class AssemblyInfoEntry : System.Xml.Serialization.IXmlSerializable
    {
        public AssemblyInfo Info { get; set; }

        public string FileName { get; set; }

        public static System.Xml.XmlQualifiedName GetMySchema(System.Xml.Schema.XmlSchemaSet xs)
        {
            ReflectionNamespace.ResolveReflectionSchema(xs);
            return new System.Xml.XmlQualifiedName("AssemblyInfoEntry", ReflectionNamespace.Data);
        }

        System.Xml.Schema.XmlSchema System.Xml.Serialization.IXmlSerializable.GetSchema()
        {
            throw new NotImplementedException();
        }

        void System.Xml.Serialization.IXmlSerializable.ReadXml(System.Xml.XmlReader reader)
        {
            if (reader.MoveToAttribute("FileName"))
            {
                this.FileName = reader.ReadContentAsString();
            }

            reader.MoveToElement();

            if (!reader.IsEmptyElement)
            {
                reader.ReadStartElement();

                var info = new AssemblyInfo();
                ((System.Xml.Serialization.IXmlSerializable)info).ReadXml(reader);
                this.Info = info;

                reader.ReadEndElement();
            }
            else
            {
                this.Info = null;
                reader.Skip();
            }
        }

        void System.Xml.Serialization.IXmlSerializable.WriteXml(System.Xml.XmlWriter writer)
        {
            writer.WriteAttributeString("FileName", this.FileName);

            writer.WriteStartElement("Info");

            if (this.Info != null)
            {
                ((System.Xml.Serialization.IXmlSerializable)this.Info).WriteXml(writer);
            }

            writer.WriteEndElement();
        }
    }
}
