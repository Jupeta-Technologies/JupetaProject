﻿using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

namespace Jupeta.Models.DBModels
{
    public class UserReg
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;
        [BsonElement("firstName")]
        public string FirstName { get; set; } = string.Empty;
        [BsonElement("lastName")]
        public string LastName { get; set; } = string.Empty;
        [BsonElement("email")]
        public string Email { get; set; } = string.Empty;
        [BsonElement("passwordHash")]
        public string PasswordHash { get; set; } = string.Empty;
        [BsonElement("phoneNumber")]
        public long? PhoneNumber { get; set; }
        [BsonElement("dateOfBirth")]
        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime DateOfBirth { get; set; }
        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime CreatedOn { get; set; }
        public DateTime ModifiedOn { get; set; }

    }
}
