import ModelBase from 'App/ModelBase';

export interface QualityProfileFormatItem {
  format: number;
  name: string;
  score: number;
  priority: boolean; // krzw(cf-priority)
}

interface CustomFormat extends ModelBase {
  name: string;
  includeCustomFormatWhenRenaming: boolean;
}

export default CustomFormat;
