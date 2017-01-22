using System;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;

namespace Circus.Fix
{
	public class Section
	{
		// TODO: remove this?
		private static readonly Dictionary<Type, Dictionary<Tag, FixFieldInfo>> tagProperties = new Dictionary<Type, Dictionary<Tag, FixFieldInfo>>();

		private static readonly Dictionary<Type, List<Tag>> allTags = new Dictionary<Type, List<Tag>>();
		private static readonly Dictionary<Type, List<Tag>> immediateTags = new Dictionary<Type, List<Tag>>();
		private static readonly Dictionary<Type, List<Tag>> immediateValueTags = new Dictionary<Type, List<Tag>>();
		private static readonly Dictionary<Type, List<Tag>> immediateGroupTags = new Dictionary<Type, List<Tag>>();

		private static readonly Dictionary<Type, List<FixFieldInfo>> immediateProperties = new Dictionary<Type, List<FixFieldInfo>>();

		public Section()
		{
			Setup();
		}

		private void Setup()
		{
			var thisType = GetType();

			if (immediateProperties.ContainsKey(thisType))
				return;


			tagProperties.Add(thisType, new Dictionary<Tag, FixFieldInfo>());
			allTags.Add(thisType, new List<Tag>());

			immediateProperties.Add(thisType, new List<FixFieldInfo>());

			immediateTags.Add(thisType, new List<Tag>());
			immediateValueTags.Add(thisType, new List<Tag>());
			immediateGroupTags.Add(thisType, new List<Tag>());

			foreach (var prop in thisType.GetRuntimeProperties())
			{
				var fieldAttr = (FieldAttribute)prop.GetCustomAttribute(typeof(FieldAttribute));

				if (fieldAttr == null)
					continue;

				if (fieldAttr is SubsectionFieldAttribute)
				{
					var fieldProp = new FixFieldInfo(prop, fieldAttr, FixFieldInfoType.Section);

					var sectionType = prop.GetValue(this).GetType();

					allTags[thisType].AddRange(allTags[sectionType]);
					immediateProperties[thisType].Add(fieldProp);
				}
				else if (fieldAttr is GroupFieldAttribute)
				{
					var fieldProp = new FixFieldInfo(prop, fieldAttr, FixFieldInfoType.Group);

					var gfa = (GroupFieldAttribute)fieldAttr;

					var groupType = prop.GetValue(this).GetType().GenericTypeArguments[0];
					// create an instance to force population of tags
					var unusedInstance = (Section)Activator.CreateInstance(groupType);

					tagProperties[thisType].Add(gfa.CountTag, fieldProp);
					allTags[thisType].AddRange(allTags[groupType]);
					allTags[thisType].Add(gfa.CountTag);
					immediateGroupTags[thisType].Add(gfa.CountTag);
					immediateTags[thisType].Add(gfa.CountTag);
					immediateProperties[thisType].Add(fieldProp);
				}
				else
				{
					var fieldProp = new FixFieldInfo(prop, fieldAttr, FixFieldInfoType.Value);

					var vfa = (ValueFieldAttribute)fieldAttr;

					tagProperties[thisType].Add(vfa.Tag, fieldProp);
					allTags[thisType].Add(vfa.Tag);
					immediateValueTags[thisType].Add(vfa.Tag);
					immediateTags[thisType].Add(vfa.Tag);
					immediateProperties[thisType].Add(fieldProp);
				}
			}

			immediateProperties[thisType] = immediateProperties[thisType].OrderBy(x => x.Order).ToList();
		}

		public byte[] Encode()
		{
			return immediateProperties[GetType()].SelectMany(x => x.Encode(this)).ToArray();
		}

		public byte[] EncodeExcept(IEnumerable<Tag> tags)
		{
			return immediateProperties[GetType()].SelectMany(x => x.EncodeExcept(this, tags)).ToArray();
		}

		public byte[] EncodeOnly(IEnumerable<Tag> tags)
		{
			return immediateProperties[GetType()].SelectMany(x => x.EncodeOnly(this,tags)).ToArray();
		}

		public byte[] EncodeExceptSections()
		{
			return immediateProperties[GetType()].SelectMany(x => x.EncodeExceptSections(this)).ToArray();
		}

		public void Decode(byte[] data)
		{
			var fields = DecodeFields(data);

			Decode(new Queue<Field>(fields));
		}

		private static IEnumerable<Field> DecodeFields(byte[] data)
		{
			var enumerator = ((IEnumerable<byte>)data).GetEnumerator();

			while (enumerator.MoveNext())
			{
				var tag = ReadTag(enumerator);
				var val = ReadValue(enumerator).ToArray();

				yield return new Field(tag, val);

				// read next if it's a byte array
				if (ByteArrayFieldAttribute.Tags.Contains(tag))
				{					
					enumerator.MoveNext();
					tag = ReadTag(enumerator);
					int length = Convert.ToInt32(Encoding.UTF8.GetString(val));
					val = ReadValue(enumerator, length).ToArray();

					yield return new Field(tag, val);
				}
			}
		}

		public void Decode(Queue<Field> fields)
		{
			var thisType = GetType();
			var tgs = new List<Tag>();

			while (fields.Count > 0)
			{
				var f = fields.Peek();

				// we're finished in this section
				if (!allTags[thisType].Contains(f.Tag))
					return;

				// if looping in a group, exit when we hit the same tag
				if (tgs.Contains(f.Tag))
					return;
				tgs.Add(f.Tag);

				// value
				if (immediateValueTags[thisType].Contains(f.Tag))
				{
					var fp = tagProperties[thisType][f.Tag];
					fp.SetValue(this, f.Data.ToArray());
					fields.Dequeue();
					continue;
				}

				// group count
				if (immediateGroupTags[thisType].Contains(f.Tag))
				{
					fields.Dequeue();

					var fp = tagProperties[thisType][f.Tag];
					Type groupType = fp.GetValue(this).GetType().GenericTypeArguments[0];
					int num = Convert.ToInt32(Encoding.UTF8.GetString(f.Data));
					var list = (IList)fp.GetValue(this);
					for (int j = 0; j < num; j++)
					{
						var section = (Section)Activator.CreateInstance(groupType);
						section.Decode(fields);
						list.Add(section);
					}

					continue;
				}

				foreach (var fixFieldInfo in immediateProperties[thisType])
				{
					if (fixFieldInfo.FfiType == FixFieldInfoType.Section)
					{
						if (!allTags[fixFieldInfo.Type].Contains(f.Tag))
							continue;

						Section sec = (Section)fixFieldInfo.GetValue(this);
						sec.Decode(fields);
						break;
					}
				}
			}
		}

		private static Tag ReadTag(IEnumerator<byte> enumerator)
		{
			int tagId = 0;

			do
			{
				if (enumerator.Current == (byte)'=')
					break;

				tagId = tagId * 10 + (enumerator.Current - 48);
			}
			while (enumerator.MoveNext());

			if (!Enum.IsDefined(typeof(Tag), tagId))
				throw new Exception("SessionRejectReason.InvalidTagNumber: " + tagId);

			enumerator.MoveNext();

			return (Tag)tagId;
		}

		private static IEnumerable<byte> ReadValue(IEnumerator<byte> enumerator)
		{
			while (enumerator.Current != 1)
			{
				yield return enumerator.Current;
				enumerator.MoveNext();
			}
		}

		private static IEnumerable<byte> ReadValue(IEnumerator<byte> enumerator, int length)
		{
			for (int i = 0; i < length;i++)
			{
				yield return enumerator.Current;
				enumerator.MoveNext();
			}
		}

		public bool Validate()
		{
			return true;
		}

		public string ToRawString()
		{
			return Encoding.UTF8.GetString(Encode()).Replace((char)1, ' ');
		}

		public string ToByteString()
		{
			return String.Join(" ", Encode().Select(b => b.ToString("D3")));
		}

		public override string ToString()
		{
			var sb = new StringBuilder();

			foreach (var fd in immediateProperties[GetType()])
			{
				var val = fd.GetValue(this);

				if (val != null)
					sb.Append(fd.ToString(val));
			}

			return sb.ToString();
		}
	}

	enum FixFieldInfoType
	{
		Value,
		Group,
		Section
	}

	class FixFieldInfo
	{
		public int Order { get { return FieldAttribute.Order; } }
		public FixFieldInfoType FfiType { get; set; }
		public Type Type { get { return PropertyInfo.PropertyType; } }

		private PropertyInfo PropertyInfo;
		private FieldAttribute FieldAttribute;

		public FixFieldInfo(PropertyInfo prop, FieldAttribute attr, FixFieldInfoType ffiType)
		{
			PropertyInfo = prop;
			FieldAttribute = attr;
			FfiType = ffiType;
		}

		public object GetValue(object obj)
		{
			return PropertyInfo.GetValue(obj);
		}

		public void SetValue(object obj, byte[] data)
		{
			var newValue = ((ValueFieldAttribute)FieldAttribute).Decode(data);

			// nullable enums need to be cast from int to the correct type
			var underlyingType = Nullable.GetUnderlyingType(PropertyInfo.PropertyType);
			if (underlyingType != null && underlyingType.GetTypeInfo().IsEnum)
			{
				newValue = Enum.Parse(underlyingType, newValue.ToString());
			}

			PropertyInfo.SetValue(obj, newValue);
		}

		public byte[] Encode(object obj)
		{
			// don't encode null values
			var val = GetValue(obj);
			if (val == null)
				return new byte[0];

			return FieldAttribute.Encode(val);
		}

		public byte[] EncodeExcept(object obj, IEnumerable<Tag> tags)
		{
			if (this.FfiType == FixFieldInfoType.Value && tags.Contains(((ValueFieldAttribute)FieldAttribute).Tag))
				return new byte[0];
			
			return Encode(obj);
		}

		public byte[] EncodeOnly(object obj, IEnumerable<Tag> tags)
		{
			if (this.FfiType == FixFieldInfoType.Value && !tags.Contains(((ValueFieldAttribute)FieldAttribute).Tag))
				return new byte[0];

			return Encode(obj);
		}

		public byte[] EncodeExceptSections(object obj)
		{
			if (this.FfiType == FixFieldInfoType.Section)
				return new byte[0];

			return Encode(obj);
		}

		public string ToString(object val)
		{
			return FieldAttribute.ToString(val);
		}
	}
}
