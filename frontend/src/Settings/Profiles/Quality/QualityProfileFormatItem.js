import PropTypes from 'prop-types';
import React, { Component } from 'react';
import CheckInput from 'Components/Form/CheckInput';
import NumberInput from 'Components/Form/NumberInput';
import styles from './QualityProfileFormatItem.css';

class QualityProfileFormatItem extends Component {

  //
  // Listeners

  onScoreChange = ({ value }) => {
    const {
      formatId
    } = this.props;

    this.props.onScoreChange(formatId, value);
  };

  onPriorityChange = ({ value }) => {
    const {
      formatId
    } = this.props;

    this.props.onPriorityChange(formatId, value);
  };

  //
  // Render

  render() {
    const {
      name,
      score,
      priority
    } = this.props;

    return (
      <div
        className={styles.qualityProfileFormatItemContainer}
      >
        <div
          className={styles.qualityProfileFormatItem}
        >
          <label
            className={styles.formatNameContainer}
          >
            <div className={styles.formatName}>
              {name}
            </div>
            <NumberInput
              containerClassName={styles.scoreContainer}
              className={styles.scoreInput}
              name={name}
              value={score}
              onChange={this.onScoreChange}
            />
            <CheckInput
              containerClassName={styles.priorityContainer}
              name={`${name}_priority`}
              value={priority}
              onChange={this.onPriorityChange}
            />
          </label>

        </div>
      </div>
    );
  }
}

QualityProfileFormatItem.propTypes = {
  formatId: PropTypes.number.isRequired,
  name: PropTypes.string.isRequired,
  score: PropTypes.number.isRequired,
  priority: PropTypes.bool.isRequired,
  onScoreChange: PropTypes.func,
  onPriorityChange: PropTypes.func
};

QualityProfileFormatItem.defaultProps = {
  // To handle the case score is deleted during edit
  score: 0,
  priority: false
};

export default QualityProfileFormatItem;
