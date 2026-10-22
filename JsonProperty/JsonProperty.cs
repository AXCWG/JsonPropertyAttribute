using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace JsonProperty;

/// <summary>
/// Mark the property to become a Json property where auto (de)serialization occurs.
/// The system works only when <c>UseJsonSerializeAttribute</c> is called in OnModelCreating. 
/// </summary>
/// <seealso cref="JsonProperty.UseJsonSerializeAttribute(ModelBuilder,JsonProperty.JsonSerializeAttributeOptions,JsonSerializerOptions)"/>
[AttributeUsage(AttributeTargets.Property)]
public class DbJsonSerializeAttribute() : Attribute
{
}

public static class JsonProperty
{
    /// <summary>
    /// Use on the database. 
    /// </summary>
    /// <remarks>Must be called in OnModelCreating. </remarks>
    /// <param name="modelBuilder">Modelbuilder in OnModelCreating.</param>
    /// <param name="attributeOptions">Options to use with JsonSerializeAttribute. </param>
    /// <param name="options">JsonSerializerOption to use in (de)serialize.</param>
    /// <typeparam name="T">Type of DBContext</typeparam>
    public static void UseJsonSerializeAttribute<T>(this ModelBuilder modelBuilder ,JsonSerializeAttributeOptions?  attributeOptions = null,JsonSerializerOptions? options = null) where T : DbContext
    {
        foreach (var propertyInfo in typeof(T)
                     .GetProperties()
                     .Where(i => i.PropertyType.IsGenericType
                                 && i.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)))
        {
            foreach (var property in propertyInfo.PropertyType.GetGenericArguments()[0].GetProperties())
            {
                if (property.CustomAttributes.Any(i => i.AttributeType == typeof(DbJsonSerializeAttribute)))
                {
                    var serializeParam = Expression.Parameter(property.PropertyType, "v");
                    var serializeMethod = typeof(JsonSerializer).GetMethod(nameof(JsonSerializer.Serialize),
                        new[] { typeof(object), typeof(Type), typeof(JsonSerializerOptions) });
                    var serializeCall = Expression.Call(serializeMethod!,
                        Expression.Convert(serializeParam, typeof(object)), Expression.Constant(property.PropertyType),
                        Expression.Constant(
                            options,
                            typeof(JsonSerializerOptions)));
                    var serializeLambda = Expression.Lambda(serializeCall, serializeParam);

                    var deserializeParam = Expression.Parameter(typeof(string), "v");
                    var deserializeMethod = typeof(JsonSerializer).GetMethod(
                        nameof(JsonSerializer.Deserialize),
                        [typeof(string), typeof(Type), typeof(JsonSerializerOptions)]
                    );
                    var deserializeCall = Expression.Call(
                        deserializeMethod!,
                        deserializeParam,
                        Expression.Constant(property.PropertyType),
                        Expression.Constant(
                            options,
                            typeof(JsonSerializerOptions))
                    );
                    var isNullOrEmpty = Expression.Call(
                        typeof(string).GetMethod(nameof(string.IsNullOrWhiteSpace), [typeof(string)])!,
                        deserializeParam
                    );
                    // 两支的最终类型都要是 property.PropertyType
                    var convertedCall = Expression.Convert(deserializeCall, property.PropertyType);
                    var defaultValue  = Expression.Default(property.PropertyType);

                    // 条件：空 -> 默认值；非空 -> 反序列化结果
                    var body = Expression.Condition(isNullOrEmpty, defaultValue, convertedCall);
                    var deserializeLambda =
                        Expression.Lambda(attributeOptions?.SetDefaultWhenNotSet??true ?  body : Expression.Convert(deserializeCall, property.PropertyType), deserializeParam);
                    // var converter = new ValueConverter<dynamic?, string>(
                    //    serializeLambda,
                    //     s => JsonSerializer.Deserialize(s, property.PropertyType)
                    // );
                    Type converterGenericType =
                        typeof(ValueConverter<,>).MakeGenericType(property.PropertyType, typeof(string));
                    var converter = Activator.CreateInstance(
                        converterGenericType,
                        serializeLambda,
                        deserializeLambda,
                        null
                    ) as ValueConverter;
                    modelBuilder.Entity(propertyInfo.PropertyType.GetGenericArguments()[0]).Property(property.Name)
                        .HasConversion(converter);
                }
            }
        }
    }

    public class JsonSerializeAttributeOptions
    {
        /// <summary>
        /// JsonSerializer cannot deserialize empty string by default. Default value of the type will be set during deserialization when string is empty or null. 
        /// </summary>
        public bool SetDefaultWhenNotSet { get; set; }
        
    }
}