// ============================================================================
// ONLINE SURVEY - MONGODB SCHEMA DE DOC
// Tuong duong file CREATE TABLE trong SQL.
// Chay: mongosh "mongodb://127.0.0.1:27017/OnlineSurvey" --file OnlineSurvey-Schema.mongodb.js
// Script KHONG xoa va KHONG chen du lieu. No tao/cap nhat collection, validator va index.
// ============================================================================

const onlineSurveyDb = db.getSiblingDB("OnlineSurvey");

function createOrUpdateCollection(collectionName, validator) {
    const exists = onlineSurveyDb
        .getCollectionInfos({ name: collectionName })
        .length > 0;

    if (!exists) {
        onlineSurveyDb.createCollection(collectionName, {
            validator,
            validationLevel: "strict",
            validationAction: "error"
        });

        print("Da tao collection: " + collectionName);
        return;
    }

    onlineSurveyDb.runCommand({
        collMod: collectionName,
        validator,
        validationLevel: "moderate",
        validationAction: "error"
    });

    print("Da cap nhat validator: " + collectionName);
}

// ============================================================================
// COLLECTION 1: admins
// Luu tai khoan quan tri. Mat khau chi luu PasswordHash.
// Quan he: admins._id (1) -> surveys.CreatedByAdminId (N)
// ============================================================================

const adminsValidator = {
    $jsonSchema: {
        bsonType: "object",
        title: "admins",
        required: [
            "Username",
            "NormalizedUsername",
            "PasswordHash",
            "DisplayName",
            "Role",
            "IsActive",
            "CreatedAt"
        ],
        properties: {
            _id: {
                bsonType: "objectId",
                description: "Khoa chinh MongoDB ObjectId"
            },
            Username: {
                bsonType: "string",
                minLength: 1,
                maxLength: 100,
                description: "Ten dang nhap"
            },
            NormalizedUsername: {
                bsonType: "string",
                minLength: 1,
                maxLength: 100,
                description: "Username viet hoa de tim kiem"
            },
            PasswordHash: {
                bsonType: "string",
                minLength: 1,
                description: "Mat khau da bam boi ASP.NET Core PasswordHasher"
            },
            DisplayName: {
                bsonType: "string",
                minLength: 1,
                maxLength: 160
            },
            Role: {
                bsonType: "string",
                enum: ["Admin"]
            },
            IsActive: {
                bsonType: "bool"
            },
            CreatedAt: {
                bsonType: "date"
            },
            LastLoginAt: {
                bsonType: ["date", "null"]
            }
        }
    }
};

// ============================================================================
// COLLECTION 2: surveys
// Moi document la mot khao sat.
// Questions va Options la embedded document, khong tach collection.
// Quan he: surveys.CreatedByAdminId -> admins._id
// ============================================================================

const surveysValidator = {
    $jsonSchema: {
        bsonType: "object",
        title: "surveys",
        required: [
            "CreatedByAdminId",
            "Slug",
            "Title",
            "Description",
            "Status",
            "Version",
            "Questions",
            "CreatedAt",
            "UpdatedAt"
        ],
        properties: {
            _id: {
                bsonType: "objectId",
                description: "Khoa chinh survey"
            },
            CreatedByAdminId: {
                bsonType: "objectId",
                description: "Tham chieu admins._id"
            },
            Slug: {
                bsonType: "string",
                minLength: 1,
                maxLength: 180,
                description: "Duong dan cong khai duy nhat"
            },
            Title: {
                bsonType: "string",
                minLength: 1,
                maxLength: 160
            },
            Description: {
                bsonType: "string",
                maxLength: 1000
            },
            Status: {
                bsonType: "string",
                enum: ["Draft", "Published"]
            },
            Version: {
                bsonType: "int",
                minimum: 1
            },
            Questions: {
                bsonType: "array",
                minItems: 1,
                maxItems: 100,
                items: {
                    bsonType: "object",
                    required: [
                        "_id",
                        "Order",
                        "Type",
                        "Text",
                        "Required",
                        "Options"
                    ],
                    properties: {
                        _id: {
                            bsonType: "string",
                            maxLength: 80,
                            description: "Question Id"
                        },
                        Order: {
                            bsonType: "int",
                            minimum: 1
                        },
                        Type: {
                            bsonType: "string",
                            enum: [
                                "text",
                                "textarea",
                                "single_choice",
                                "multiple_choice"
                            ]
                        },
                        Text: {
                            bsonType: "string",
                            minLength: 1,
                            maxLength: 500
                        },
                        Required: {
                            bsonType: "bool"
                        },
                        Options: {
                            bsonType: "array",
                            maxItems: 50,
                            items: {
                                bsonType: "object",
                                required: ["_id", "Text"],
                                properties: {
                                    _id: {
                                        bsonType: "string",
                                        description: "Option Id"
                                    },
                                    Text: {
                                        bsonType: "string",
                                        minLength: 1,
                                        maxLength: 250
                                    }
                                }
                            }
                        }
                    }
                }
            },
            CreatedAt: {
                bsonType: "date"
            },
            UpdatedAt: {
                bsonType: "date"
            }
        }
    }
};

// ============================================================================
// COLLECTION 3: responses
// Moi document la mot lan gui khao sat.
// Answers la embedded document, khong tach collection.
// Quan he: responses.SurveyId -> surveys._id
// ============================================================================

const responsesValidator = {
    $jsonSchema: {
        bsonType: "object",
        title: "responses",
        required: [
            "SurveyId",
            "SurveyVersion",
            "Answers",
            "SubmittedAt"
        ],
        properties: {
            _id: {
                bsonType: "objectId",
                description: "Khoa chinh response"
            },
            SurveyId: {
                bsonType: "objectId",
                description: "Tham chieu surveys._id"
            },
            SurveyVersion: {
                bsonType: "int",
                minimum: 1,
                description: "Phien ban survey luc nguoi dung gui"
            },
            RespondentName: {
                bsonType: "string",
                maxLength: 160
            },
            Answers: {
                bsonType: "array",
                items: {
                    bsonType: "object",
                    required: ["QuestionId", "Values"],
                    properties: {
                        QuestionId: {
                            bsonType: "string",
                            description: "Tham chieu Questions._id trong survey"
                        },
                        Values: {
                            bsonType: "array",
                            items: {
                                bsonType: "string"
                            }
                        }
                    }
                }
            },
            SubmittedAt: {
                bsonType: "date"
            }
        }
    }
};

// Tao hoac cap nhat 3 collection.
createOrUpdateCollection("admins", adminsValidator);
createOrUpdateCollection("surveys", surveysValidator);
createOrUpdateCollection("responses", responsesValidator);

// ============================================================================
// INDEXES
// Tuong duong CREATE UNIQUE INDEX / CREATE INDEX trong SQL.
// ============================================================================

onlineSurveyDb.admins.createIndex(
    { NormalizedUsername: 1 },
    {
        name: "NormalizedUsername_1",
        unique: true
    }
);

onlineSurveyDb.surveys.createIndex(
    { Slug: 1 },
    {
        name: "Slug_1",
        unique: true
    }
);

onlineSurveyDb.responses.createIndex(
    {
        SurveyId: 1,
        SubmittedAt: -1
    },
    {
        name: "SurveyId_1_SubmittedAt_-1"
    }
);

// ============================================================================
// QUAN HE LOGIC
//
// admins._id  (1) -------- (N) surveys.CreatedByAdminId
// surveys._id (1) -------- (N) responses.SurveyId
//
// MongoDB khong tao FOREIGN KEY. Ung dung ASP.NET Core kiem soat quan he.
// ============================================================================

print("");
print("Khoi tao MongoDB schema thanh cong.");
print("Database: OnlineSurvey");
print("Collections: admins, surveys, responses");
print("admins indexes: " + onlineSurveyDb.admins.getIndexes().length);
print("surveys indexes: " + onlineSurveyDb.surveys.getIndexes().length);
print("responses indexes: " + onlineSurveyDb.responses.getIndexes().length);

