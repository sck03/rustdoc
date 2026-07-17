using System;
using System.ComponentModel;
using System.Linq;

namespace ExportDocManager.Utils
{
    [AttributeUsage(AttributeTargets.Property)]
    public class PropertyOrderAttribute : Attribute
    {
        public int Order { get; }

        public PropertyOrderAttribute(int order)
        {
            Order = order;
        }
    }

    public class OrderedTypeDescriptionProvider : TypeDescriptionProvider
    {
        private static readonly TypeDescriptionProvider DefaultProvider = TypeDescriptor.GetProvider(typeof(object));

        public OrderedTypeDescriptionProvider()
            : base(DefaultProvider)
        {
        }

        public override ICustomTypeDescriptor GetTypeDescriptor(Type objectType, object instance)
        {
            var baseDescriptor = base.GetTypeDescriptor(objectType, instance);
            return new OrderedTypeDescriptor(baseDescriptor);
        }
    }

    public class OrderedTypeDescriptor : CustomTypeDescriptor
    {
        public OrderedTypeDescriptor(ICustomTypeDescriptor parent)
            : base(parent)
        {
        }

        public override PropertyDescriptorCollection GetProperties()
        {
            return GetProperties([]);
        }

        public override PropertyDescriptorCollection GetProperties(Attribute[] attributes)
        {
            var pdc = base.GetProperties(attributes);
            var orderedProperties = pdc.Cast<PropertyDescriptor>()
                .Select(pd =>
                {
                    var attribute = pd.Attributes[typeof(PropertyOrderAttribute)] as PropertyOrderAttribute;
                    return attribute != null ? new OrderedPropertyDescriptor(pd, attribute.Order) : pd;
                })
                .OrderBy(pd =>
                {
                    var wrapped = pd as OrderedPropertyDescriptor;
                    return wrapped?.Order ?? int.MaxValue;
                })
                .ThenBy(pd => pd.DisplayName)
                .ToArray();

            return new PropertyDescriptorCollection(orderedProperties);
        }
    }

    public class OrderedPropertyDescriptor : PropertyDescriptor
    {
        private readonly PropertyDescriptor _baseDescriptor;
        public int Order { get; }

        public OrderedPropertyDescriptor(PropertyDescriptor baseDescriptor, int order)
            : base(baseDescriptor)
        {
            _baseDescriptor = baseDescriptor;
            Order = order;
        }

        public override string DisplayName => new string('\u200B', Order) + _baseDescriptor.DisplayName;
        public override Type ComponentType => _baseDescriptor.ComponentType;
        public override bool IsReadOnly => _baseDescriptor.IsReadOnly;
        public override Type PropertyType => _baseDescriptor.PropertyType;
        public override bool CanResetValue(object component) => _baseDescriptor.CanResetValue(component);
        public override object GetValue(object component) => _baseDescriptor.GetValue(component);
        public override void ResetValue(object component) => _baseDescriptor.ResetValue(component);
        public override void SetValue(object component, object value) => _baseDescriptor.SetValue(component, value);
        public override bool ShouldSerializeValue(object component) => _baseDescriptor.ShouldSerializeValue(component);
        public override string Description => _baseDescriptor.Description;
        public override string Category => _baseDescriptor.Category;
        public override TypeConverter Converter => _baseDescriptor.Converter;
        public override AttributeCollection Attributes => _baseDescriptor.Attributes;
        public override object GetEditor(Type editorBaseType) => _baseDescriptor.GetEditor(editorBaseType);
    }
}
