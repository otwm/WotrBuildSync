export type AbilityShort = "STR" | "DEX" | "CON" | "INT" | "WIS" | "CHA";

export type AbilityScore = {
  short: AbilityShort;
  base: number;
  final: number;
  note?: string;
};

export type ClassEntry = {
  name: string;
  level: number;
  archetype?: string;
};

export type LevelStep = {
  level: number;
  detail: string;
};

export type SpellGroup = {
  circle: number;
  spells: string[];
};

export type PetSpecies =
  | "dog"
  | "giant centipede"
  | "smilodon"
  | "bear"
  | "wolf"
  | "mastodon"
  | "horse"
  | "boar"
  | "velociraptor"
  | "elk"
  | "monitor_lizard"
  | "triceratops"
  | "leopard";

export type PetType =
  | "deathtouched"
  | "daredevil"
  | "racer"
  | "wrecker"
  | "bully"
  | "bulwark"
  | "aggressor";

export type PetBuild = {
  name: string;
  species: PetSpecies;
  kind: PetType;
  levelPath: LevelStep[];
  stats: AbilityScore[];
  notes: string;
  updatedAt: string;
  createdAt: string;
  version?: string;
  lang: "ko" | "en";
};

export type Role =
  | "melee-dps"
  | "ranged-dps"
  | "tank"
  | "healer"
  | "caster"
  | "buffer"
  | "hybrid"
  | "dispel";

export type Build = {
  /** 로그인 아이디 */
  creator?: string;
  /** 캐릭터 닉네임 */
  creatorNickname?: string;
  password?: string;
  createdAt?: string;
  /** URL-safe slug, e.g. "arueshalae-ranger" */
  slug: string;
  /** Character display name (Korean) */
  characterName: string;
  /** Character display name (English) */
  characterNameEn: string;
  /** Build subtitle / archetype label */
  buildTitle: string;
  /** Companion or main characters */
  type: "companion" | "main" | "mercenary";
  /** Combat role */
  role: Role[];
  /** Hero portrait image URL */
  portraitUrl: string;
  /** Concept summary lines */
  description: {
    concept: string;
    power: string;
    defense: string;
    notes: string;
  };
  /** 배경 */
  background?: string;
  /** 종족 */
  race?: string;
  /** Class composition (mostly single class but supports multiclass) */
  classes: ClassEntry[];
  /** Ability scores */
  abilities: AbilityScore[];
  /** Feats list (Korean(English) format) */
  feats: string[];
  /**
   * Mythic feats
   * @deprecated
   */
  mythicFeats?: string[];
  /**
   * Mythic abilities
   * @deprecated
   */
  mythicAbilities?: string[];
  mythicLevelPath?: string[];
  /** Leveling path from level 9-20 (or full path) */
  levelPath: LevelStep[];
  /** Recommended spells by circle */
  recommendedSpells: SpellGroup[];
  /** Tags for filtering */
  tags: string[];
  /** Last updated date */
  updatedAt: string;
  /** 연결된 펫 빌드 id (없으면 링크 숨김) */
  petBuildId?: string;
  /** 빌드 작성 언어 */
  lang?: "ko" | "en";
  /** 반대 언어 번역본의 slug (null = 번역본 없음) */
  translationOf?: string | null;
  /** 버전 */
  version?: string;
};
