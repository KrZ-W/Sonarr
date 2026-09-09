// krzw(imdb-title-provider): IMDb Title Provider settings on the Metadata page
import React, { useCallback, useEffect } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { createSelector } from 'reselect';
import AppState from 'App/State/AppState';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import { inputTypes, kinds } from 'Helpers/Props';
import { clearPendingChanges } from 'Store/Actions/baseActions';
import {
  fetchMetadataOptions,
  saveMetadataOptions,
  setMetadataOptionsValue,
} from 'Store/Actions/settingsActions';
import createSettingsSectionSelector from 'Store/Selectors/createSettingsSectionSelector';
import translate from 'Utilities/String/translate';

const SECTION = 'metadataOptions';

function createMetadataOptionsSelector() {
  return createSelector(
    (state: AppState) => state.settings.advancedSettings,
    createSettingsSectionSelector(SECTION),
    (advancedSettings, sectionSettings) => {
      return {
        advancedSettings,
        save: sectionSettings.isSaving,
        ...sectionSettings,
      };
    }
  );
}

interface MetadataOptionsProps {
  setChildSave(saveCallback: () => void): void;
  onChildStateChange(payload: unknown): void;
}

function MetadataOptions(props: MetadataOptionsProps) {
  const { setChildSave, onChildStateChange } = props;

  const {
    isSaving,
    hasPendingChanges,
    isFetching,
    error,
    settings,
    hasSettings,
  } = useSelector(createMetadataOptionsSelector());

  const dispatch = useDispatch();

  const onInputChange = useCallback(
    ({ name, value }: { name: string; value: unknown }) => {
      // @ts-expect-error 'setMetadataOptionsValue' isn't typed yet
      dispatch(setMetadataOptionsValue({ name, value }));
    },
    [dispatch]
  );

  useEffect(() => {
    dispatch(fetchMetadataOptions());
    setChildSave(() => dispatch(saveMetadataOptions()));

    return () => {
      dispatch(clearPendingChanges({ section: SECTION }));
    };
  }, [dispatch, setChildSave]);

  useEffect(() => {
    onChildStateChange({
      isSaving,
      hasPendingChanges,
    });
  }, [onChildStateChange, isSaving, hasPendingChanges]);

  return (
    <FieldSet legend={translate('ImdbTitleProvider')}>
      {isFetching ? <LoadingIndicator /> : null}

      {!isFetching && error ? (
        <Alert kind={kinds.DANGER}>
          {translate('MetadataOptionsLoadError')}
        </Alert>
      ) : null}

      {hasSettings && !isFetching && !error ? (
        <Form>
          <FormGroup>
            <FormLabel>{translate('ImdbTitleProviderEnabled')}</FormLabel>
            <FormInputGroup
              type={inputTypes.CHECK}
              name="imdbTitleProviderEnabled"
              helpText={translate('ImdbTitleProviderEnabledHelpText')}
              onChange={onInputChange}
              {...settings.imdbTitleProviderEnabled}
            />
          </FormGroup>

          <FormGroup>
            <FormLabel>{translate('ImdbTitleProviderRegions')}</FormLabel>
            <FormInputGroup
              type={inputTypes.TEXT}
              name="imdbTitleProviderRegions"
              helpText={translate('ImdbTitleProviderRegionsHelpText')}
              onChange={onInputChange}
              {...settings.imdbTitleProviderRegions}
            />
          </FormGroup>

          <FormGroup>
            <FormLabel>{translate('ImdbTitleProviderLanguages')}</FormLabel>
            <FormInputGroup
              type={inputTypes.TEXT}
              name="imdbTitleProviderLanguages"
              helpText={translate('ImdbTitleProviderLanguagesHelpText')}
              onChange={onInputChange}
              {...settings.imdbTitleProviderLanguages}
            />
          </FormGroup>

          <FormGroup>
            <FormLabel>
              {translate('ImdbTitleProviderRefreshInterval')}
            </FormLabel>
            <FormInputGroup
              type={inputTypes.NUMBER}
              name="imdbTitleProviderRefreshInterval"
              min={1}
              unit={translate('Days')}
              helpText={translate('ImdbTitleProviderRefreshIntervalHelpText')}
              onChange={onInputChange}
              {...settings.imdbTitleProviderRefreshInterval}
            />
          </FormGroup>
        </Form>
      ) : null}
    </FieldSet>
  );
}

export default MetadataOptions;
