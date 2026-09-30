import classNames from 'classnames';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import CheckInput from 'Components/Form/CheckInput';
import Icon from 'Components/Icon';
import IconButton from 'Components/Link/IconButton';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import QualityProfileItemSizeLimits from './QualityProfileItemSizeLimits'; // krzw(profile-size-limits)
import styles from './QualityProfileItem.css';

class QualityProfileItem extends Component {

  //
  // Listeners

  onAllowedChange = ({ value }) => {
    const {
      qualityId,
      onQualityProfileItemAllowedChange
    } = this.props;

    onQualityProfileItemAllowedChange(qualityId, value);
  };

  // krzw(profile-size-limits)
  onSizeLimitChange = (name, value) => {
    const {
      qualityId,
      onQualityProfileItemSizeLimitChange
    } = this.props;

    onQualityProfileItemSizeLimitChange(qualityId, false, name, value);
  };

  onCreateGroupPress = () => {
    const {
      qualityId,
      onCreateGroupPress
    } = this.props;

    onCreateGroupPress(qualityId);
  };

  //
  // Render

  render() {
    const {
      editGroups,
      isPreview,
      groupId,
      name,
      allowed,
      minSize, // krzw(profile-size-limits)
      preferredSize, // krzw(profile-size-limits)
      maxSize, // krzw(profile-size-limits)
      isDragging,
      isOverCurrent,
      connectDragSource
    } = this.props;

    return (
      <div
        className={classNames(
          styles.qualityProfileItem,
          isDragging && styles.isDragging,
          isPreview && styles.isPreview,
          isOverCurrent && styles.isOverCurrent,
          groupId && styles.isInGroup
        )}
      >
        <label
          className={styles.qualityNameContainer}
        >
          {
            editGroups && !groupId && !isPreview &&
              <IconButton
                className={styles.createGroupButton}
                name={icons.GROUP}
                title={translate('Group')}
                onPress={this.onCreateGroupPress}
              />
          }

          {
            !editGroups &&
              <CheckInput
                className={styles.checkInput}
                containerClassName={styles.checkInputContainer}
                name={name}
                value={allowed}
                isDisabled={!!groupId}
                onChange={this.onAllowedChange}
              />
          }

          <div className={classNames(
            styles.qualityName,
            groupId && styles.isInGroup,
            !allowed && styles.notAllowed
          )}
          >
            {name}
          </div>
        </label>

        {
          // krzw(profile-size-limits): overrides only editable outside group-edit mode and outside the drag preview
          !editGroups && !isPreview && !groupId &&
            <QualityProfileItemSizeLimits
              minSize={minSize}
              preferredSize={preferredSize}
              maxSize={maxSize}
              onSizeLimitChange={this.onSizeLimitChange}
            />
        }

        {
          connectDragSource(
            <div className={styles.dragHandle}>
              <Icon
                className={styles.dragIcon}
                title={translate('CreateGroup')}
                name={icons.REORDER}
              />
            </div>
          )
        }
      </div>
    );
  }
}

QualityProfileItem.propTypes = {
  editGroups: PropTypes.bool,
  isPreview: PropTypes.bool,
  groupId: PropTypes.number,
  qualityId: PropTypes.number.isRequired,
  name: PropTypes.string.isRequired,
  allowed: PropTypes.bool.isRequired,
  minSize: PropTypes.number, // krzw(profile-size-limits)
  preferredSize: PropTypes.number, // krzw(profile-size-limits)
  maxSize: PropTypes.number, // krzw(profile-size-limits)
  isDragging: PropTypes.bool.isRequired,
  isOverCurrent: PropTypes.bool.isRequired,
  isInGroup: PropTypes.bool,
  connectDragSource: PropTypes.func,
  onCreateGroupPress: PropTypes.func,
  onQualityProfileItemAllowedChange: PropTypes.func,
  onQualityProfileItemSizeLimitChange: PropTypes.func // krzw(profile-size-limits)
};

QualityProfileItem.defaultProps = {
  isPreview: false,
  isOverCurrent: false,
  // The drag preview will not connect the drag handle.
  connectDragSource: (node) => node
};

export default QualityProfileItem;
