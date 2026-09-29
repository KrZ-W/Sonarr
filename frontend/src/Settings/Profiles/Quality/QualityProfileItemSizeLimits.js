// krzw(profile-size-limits)
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import NumberInput from 'Components/Form/NumberInput';
import translate from 'Utilities/String/translate';
import styles from './QualityProfileItemSizeLimits.css';

// Per-quality (or per-group) MB/min overrides of the global quality definition
// size limits. Empty = inherit the global value; a max of 0 = unlimited.
class QualityProfileItemSizeLimits extends Component {

  //
  // Listeners

  onChange = ({ name, value }) => {
    this.props.onSizeLimitChange(name, value);
  };

  //
  // Render

  render() {
    const {
      minSize,
      preferredSize,
      maxSize
    } = this.props;

    return (
      <div
        className={styles.sizeLimits}
        title={translate('QualityProfileSizeLimitsHelpText')}
      >
        <NumberInput
          className={styles.sizeInput}
          name="minSize"
          value={minSize}
          placeholder={translate('Min')}
          min={0}
          max={1000}
          isFloat={true}
          onChange={this.onChange}
        />

        <NumberInput
          className={styles.sizeInput}
          name="preferredSize"
          value={preferredSize}
          placeholder={translate('Preferred')}
          min={0}
          max={1000}
          isFloat={true}
          onChange={this.onChange}
        />

        <NumberInput
          className={styles.sizeInput}
          name="maxSize"
          value={maxSize}
          placeholder={translate('Max')}
          min={0}
          max={1000}
          isFloat={true}
          onChange={this.onChange}
        />
      </div>
    );
  }
}

QualityProfileItemSizeLimits.propTypes = {
  minSize: PropTypes.number,
  preferredSize: PropTypes.number,
  maxSize: PropTypes.number,
  onSizeLimitChange: PropTypes.func.isRequired
};

export default QualityProfileItemSizeLimits;
