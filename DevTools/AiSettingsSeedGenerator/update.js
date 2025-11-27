const fs = require('fs');
const path = require('path');

const baseDir = __dirname;
const [, , ...args] = process.argv;
const [apiKey] = args;

if (args.includes('--help')) {
    console.log('Usage: node update.js [API_KEY]');
    process.exit(0);
}

const toJson = (data) => {
    let json = JSON.stringify(data).replace(/'/g, "''");
    if (apiKey) {
        json = json.replaceAll('###OPEN_AI_SETTINGS.API_KEY###', apiKey);
    }
    return json;
};

const getSqlTemplate = (type, settings, config) => `

-- START ${type} UPSERT

UPDATE ai.ai_settings
SET 
    settings = '${toJson(settings)}'::jsonb,
    updated_at = now()
WHERE "type" = '${type}';

INSERT INTO ai.ai_settings (client_id, "type", status, settings, config,
                            created_at, created_by, updated_at, updated_by, deleted_at, deleted_by)
SELECT 1,
       '${type}',
       'Enabled',
       '${toJson(settings)}'::jsonb,
       '${toJson(config)}'::jsonb,
        now(),
       '00000000-0000-0000-0000-000000000000'::uuid,
        now(),
       '00000000-0000-0000-0000-000000000000'::uuid,
       NULL,
       NULL
WHERE NOT EXISTS (SELECT 1
                  FROM ai.ai_settings
                  WHERE "type" = '${type}');
                  
-- END ${type} UPSERT
`;

const readFileContent = (filePath) => {
    try {
        return fs.existsSync(filePath) ? fs.readFileSync(filePath, 'utf8').trim() : "";
    } catch (err) {
        console.error(`Error reading file ${filePath}:`, err);
        return "";
    }
};

const directories = fs.readdirSync(baseDir, {withFileTypes: true})
    .filter(dirent => dirent.isDirectory())
    .map(dirent => dirent.name);

const allowedExtensions = ['.liquid', '.md'];
const results = {};

for (const dir of directories) {
    const dirPath = path.join(baseDir, dir);
    let dirContent;
    try {
        dirContent = fs.readdirSync(dirPath);
    } catch (err) {
        console.error(`Error reading directory ${dirPath}:`, err);
        process.exit(1);
    }

    if (dirContent.length === 0) {
        continue;
    }

    const templateNames = dirContent.filter(x => allowedExtensions.some(ext => x.endsWith(ext)));

    const settings = {
        default: {}
    };

    for (const templateName of templateNames) {
        const templatePath = path.join(dirPath, templateName);
        const content = readFileContent(templatePath);
        settings.default[path.basename(templateName, path.extname(templateName))] = content;
    }

    const configPath = path.join(dirPath, 'config.json');
    let config;
    try {
        config = JSON.parse(fs.readFileSync(configPath, 'utf8'));
    } catch (err) {
        console.error(`Error parsing JSON in ${configPath}:`, err);
        process.exit(1);
    }

    if (apiKey) {
        config.ApiKey = apiKey;
    }

    results[dir] = {settings, config};
}

try {
    fs.writeFileSync(
        path.join(baseDir, 'upsert.sql'),
        Object
            .keys(results)
            .map(type => getSqlTemplate(type, results[type].settings, results[type].config))
            .join(''),
        'utf8'
    );
    console.log("SQL upsert script has been updated successfully.");
} catch (err) {
    console.error('Error writing upsert.sql:', err);
    process.exit(1);
}

const now = new Date();
const seedDataPath = path.resolve(baseDir, '..', '..', 'HC.AiProcessor.Infrastructure', 'Seed', 'AiSettingsSeed.json');
const seedData = [];
let isSeedDataChanged = false;

function pushSeedData(type, createdAt, result) {
    seedData.push({
        type,
        createdAt,
        ...result
    });
}

if (fs.existsSync(seedDataPath)) {
    let oldSeedData;
    try {
        oldSeedData = JSON.parse(fs.readFileSync(seedDataPath, 'utf8'));
    } catch (err) {
        console.error(`Error parsing JSON in ${seedDataPath}:`, err);
        console.error('Defaulting to empty seed data due to JSON parse error.');
        oldSeedData = [];
    }

    for (const [type, result] of Object.entries(results)) {
        const oldAiSettings = oldSeedData.find(x => x.type === type);
        if (!oldAiSettings) {
            pushSeedData(type, now.toISOString(), result);
            isSeedDataChanged = true;
            continue;
        }

        const oldData = {config: oldAiSettings.config, settings: oldAiSettings.settings};
        const newData = {config: result.config, settings: result.settings};

        if (JSON.stringify(oldData) !== JSON.stringify(newData)) {
            isSeedDataChanged = true;
            pushSeedData(type, now.toISOString(), result);
        } else {
            pushSeedData(type, oldAiSettings.createdAt, result);
        }
    }
} else {
    for (const [type, result] of Object.entries(results)) {
        pushSeedData(type, now.toISOString(), result);
    }
    isSeedDataChanged = true;
}

if (isSeedDataChanged) {
    try {
        fs.writeFileSync(seedDataPath, JSON.stringify(seedData, null, 2), 'utf8');
        console.log("JSON seed data has been updated successfully.");
    } catch (err) {
        console.error('Error writing seed data JSON:', err);
        process.exit(1);
    }
} else {
    console.log("JSON seed data is already up to date. No changes made.");
}
